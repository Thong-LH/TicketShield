-- ==========================================================
-- TICKETSHIELD & ORGANIZER MULTI-DATABASE INITIALIZATION
-- ==========================================================

-- 1. CREATE DATABASES IF NOT EXIST
SELECT 'CREATE DATABASE organizer_db' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'organizer_db')\gexec
SELECT 'CREATE DATABASE ticketshield_db' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'ticketshield_db')\gexec
SELECT 'CREATE DATABASE identity_db' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'identity_db')\gexec

-- 2. CONNECT TO ORGANIZER DATABASE & CREATE TABLES
\c organizer_db;

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Table 1: mock_tickets
CREATE TABLE IF NOT EXISTS mock_tickets (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    ticket_code VARCHAR(50) UNIQUE NOT NULL,
    event_name VARCHAR(255) NOT NULL,
    seat_zone VARCHAR(100) NOT NULL,
    original_price NUMERIC(15, 2) NOT NULL,
    owner_email VARCHAR(255) NOT NULL,
    owner_phone VARCHAR(50),
    owner_name VARCHAR(255),
    status VARCHAR(50) NOT NULL DEFAULT 'VALID', -- VALID, LOCKED_FOR_RESALE, TRANSFERRED, USED, CANCELLED
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Table 2: mock_otps
CREATE TABLE IF NOT EXISTS mock_otps (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    ticket_code VARCHAR(50) NOT NULL,
    owner_email VARCHAR(255) NOT NULL,
    otp_code VARCHAR(10) NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    is_used BOOLEAN NOT NULL DEFAULT FALSE,
    attempts INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_mock_otps_lookup ON mock_otps(ticket_code, owner_email, is_used);

-- Table 3: gate_access_logs
CREATE TABLE IF NOT EXISTS gate_access_logs (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    ticket_code VARCHAR(50) NOT NULL,
    gate_name VARCHAR(100) NOT NULL,
    scanned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    scan_result VARCHAR(50) NOT NULL, -- SUCCESS, DUPLICATE_ENTRY, INVALID_TICKET, REVOKED
    scanner_device_id VARCHAR(100),
    notes TEXT
);
CREATE INDEX idx_gate_logs_ticket ON gate_access_logs(ticket_code);

-- SEED DATA FOR ORGANIZER DB
INSERT INTO mock_tickets (id, ticket_code, event_name, seat_zone, original_price, owner_email, owner_phone, owner_name, status)
VALUES 
    ('a0000000-0000-0000-0000-000000000001', 'ATSH-VIP-888', 'Anh Trai Say Hi Concert 2026', 'VIP Zone A - Row 1 Seat 12', 2500000, 'seller@ticketshield.vn', '0901234567', 'Nguyen Van Seller', 'VALID'),
    ('a0000000-0000-0000-0000-000000000002', 'ATSH-GA-999', 'Anh Trai Say Hi Concert 2026', 'GA Standing Zone 2', 1200000, 'seller@ticketshield.vn', '0901234567', 'Nguyen Van Seller', 'VALID'),
    ('a0000000-0000-0000-0000-000000000003', 'ATSH-USED-001', 'Anh Trai Say Hi Concert 2026', 'Standard Zone C', 800000, 'seller@ticketshield.vn', '0901234567', 'Nguyen Van Seller', 'USED')
ON CONFLICT (ticket_code) DO NOTHING;


-- ==========================================================
-- 3. CONNECT TO TICKETSHIELD DATABASE (Core P2P & Escrow)
-- ==========================================================
\c ticketshield_db;

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Table 4: users
CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    email VARCHAR(255) UNIQUE NOT NULL,
    full_name VARCHAR(255) NOT NULL,
    phone_number VARCHAR(50),
    id_card_number VARCHAR(255),
    role VARCHAR(50) NOT NULL DEFAULT 'USER', -- USER, ADMIN, CSKH, ORGANIZER
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Table 5: user_bank_accounts
CREATE TABLE IF NOT EXISTS user_bank_accounts (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    bank_code VARCHAR(50) NOT NULL,
    bank_name VARCHAR(255) NOT NULL,
    account_number VARCHAR(100) NOT NULL,
    account_holder_name VARCHAR(255) NOT NULL,
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_user_bank_accounts_user ON user_bank_accounts(user_id);

-- Table 6: organizers
CREATE TABLE IF NOT EXISTS organizers (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name VARCHAR(255) NOT NULL,
    official_email VARCHAR(255) UNIQUE NOT NULL,
    contact_phone VARCHAR(50),
    api_key_hash VARCHAR(255) NOT NULL,
    webhook_url VARCHAR(500),
    status VARCHAR(50) NOT NULL DEFAULT 'ACTIVE',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Table 7: events
CREATE TABLE IF NOT EXISTS events (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    organizer_id UUID NOT NULL REFERENCES organizers(id) ON DELETE RESTRICT,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    venue VARCHAR(500) NOT NULL,
    event_start_at TIMESTAMPTZ NOT NULL,
    event_end_at TIMESTAMPTZ NOT NULL,
    resale_deadline TIMESTAMPTZ NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'UPCOMING',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Table 8: ticket_tiers
CREATE TABLE IF NOT EXISTS ticket_tiers (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    event_id UUID NOT NULL REFERENCES events(id) ON DELETE CASCADE,
    tier_name VARCHAR(100) NOT NULL,
    original_price NUMERIC(15, 2) NOT NULL,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Table 9: resale_listings (P2P Core)
CREATE TABLE IF NOT EXISTS resale_listings (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    event_id UUID NOT NULL REFERENCES events(id) ON DELETE RESTRICT,
    tier_id UUID NOT NULL REFERENCES ticket_tiers(id) ON DELETE RESTRICT,
    seller_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    original_ticket_code VARCHAR(100) NOT NULL,
    original_price NUMERIC(15, 2) NOT NULL,
    resale_price NUMERIC(15, 2) NOT NULL,
    is_private BOOLEAN NOT NULL DEFAULT FALSE,
    private_access_token VARCHAR(255),
    verification_status VARCHAR(50) NOT NULL DEFAULT 'VERIFIED', -- PENDING_OTP, VERIFIED, REJECTED
    listing_status VARCHAR(50) NOT NULL DEFAULT 'VERIFIED', -- DRAFT, VERIFIED, TRANSACTING, SOLD, CANCELLED
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Partial Unique Index: Only one active listing per original ticket code
CREATE UNIQUE INDEX IF NOT EXISTS idx_unique_active_ticket 
ON resale_listings (original_ticket_code) 
WHERE listing_status IN ('VERIFIED', 'TRANSACTING');

CREATE INDEX IF NOT EXISTS idx_resale_listings_filter 
ON resale_listings(event_id, tier_id, listing_status, is_private);

-- Table 10: escrow_transactions
CREATE TABLE IF NOT EXISTS escrow_transactions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    listing_id UUID NOT NULL REFERENCES resale_listings(id) ON DELETE RESTRICT,
    buyer_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    seller_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    original_ticket_price NUMERIC(15, 2) NOT NULL,
    buyer_fee NUMERIC(15, 2) NOT NULL DEFAULT 0,
    seller_fee NUMERIC(15, 2) NOT NULL DEFAULT 0,
    total_buyer_paid NUMERIC(15, 2) NOT NULL,
    net_seller_payout NUMERIC(15, 2) NOT NULL,
    payment_reference VARCHAR(255) UNIQUE,
    status VARCHAR(50) NOT NULL DEFAULT 'PENDING', -- PENDING, LOCKED, RELEASED, REFUNDED, DISPUTED
    unlock_at TIMESTAMPTZ,
    dispute_deadline TIMESTAMPTZ,
    recipient_name VARCHAR(255),
    recipient_email VARCHAR(255),
    recipient_id_card VARCHAR(255),
    new_ticket_code VARCHAR(255),
    qr_code_data TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Table 11: payout_transactions
CREATE TABLE IF NOT EXISTS payout_transactions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    escrow_id UUID UNIQUE NOT NULL REFERENCES escrow_transactions(id) ON DELETE RESTRICT,
    seller_bank_account_id UUID REFERENCES user_bank_accounts(id) ON DELETE SET NULL,
    payout_code VARCHAR(100) UNIQUE NOT NULL,
    recipient_bank_code VARCHAR(50) NOT NULL,
    recipient_account_number VARCHAR(100) NOT NULL,
    recipient_account_name VARCHAR(255) NOT NULL,
    amount NUMERIC(15, 2) NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'PENDING', -- PENDING, PROCESSING, SUCCESS, FAILED
    bank_reference_code VARCHAR(255),
    retry_count INT NOT NULL DEFAULT 0,
    last_error_message TEXT,
    processed_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Table 12: disputes
CREATE TABLE IF NOT EXISTS disputes (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    escrow_id UUID UNIQUE NOT NULL REFERENCES escrow_transactions(id) ON DELETE RESTRICT,
    buyer_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    dispute_code VARCHAR(100) UNIQUE NOT NULL,
    reason_code VARCHAR(100) NOT NULL, -- TICKET_INVALID, DUPLICATE_ENTRY, FAKE
    description TEXT NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'OPEN', -- OPEN, UNDER_REVIEW, RESOLVED, REJECTED
    resolution VARCHAR(50), -- REFUND_BUYER, RELEASE_SELLER
    refund_amount NUMERIC(15, 2) DEFAULT 0,
    admin_notes TEXT,
    resolved_by UUID REFERENCES users(id) ON DELETE SET NULL,
    resolved_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Table 13: dispute_evidences
CREATE TABLE IF NOT EXISTS dispute_evidences (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    dispute_id UUID NOT NULL REFERENCES disputes(id) ON DELETE CASCADE,
    uploader_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    evidence_type VARCHAR(50) NOT NULL, -- IMAGE, VIDEO, GATE_REPORT_DOC
    file_url VARCHAR(1000) NOT NULL,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Table 14: dispute_messages
CREATE TABLE IF NOT EXISTS dispute_messages (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    dispute_id UUID NOT NULL REFERENCES disputes(id) ON DELETE CASCADE,
    sender_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    sender_role VARCHAR(50) NOT NULL, -- BUYER, SELLER, ADMIN, CSKH
    message_type VARCHAR(50) NOT NULL DEFAULT 'TEXT',
    content TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- SEED DATA FOR TICKETSHIELD DB
INSERT INTO users (id, email, full_name, phone_number, role, is_active)
VALUES 
    ('11111111-1111-1111-1111-111111111111', 'seller@ticketshield.vn', 'Nguyen Van Seller', '0901234567', 'USER', true),
    ('22222222-2222-2222-2222-222222222222', 'buyer@ticketshield.vn', 'Tran Thi Buyer', '0987654321', 'USER', true),
    ('99999999-9999-9999-9999-999999999999', 'admin@ticketshield.vn', 'System Administrator', '0999999999', 'ADMIN', true)
ON CONFLICT (email) DO NOTHING;

INSERT INTO organizers (id, name, official_email, contact_phone, api_key_hash, webhook_url, status)
VALUES 
    ('e0000000-0000-0000-0000-000000000001', 'VieON Entertainment', 'contact@vieon.vn', '19001234', 'mock_api_key_hash_123456', 'http://localhost:5001/api/v1/webhook', 'ACTIVE')
ON CONFLICT (official_email) DO NOTHING;

INSERT INTO events (id, organizer_id, name, description, venue, event_start_at, event_end_at, resale_deadline, status)
VALUES 
    ('e1111111-1111-1111-1111-111111111111', 'e0000000-0000-0000-0000-000000000001', 'Anh Trai Say Hi Concert 2026', 'Mega Concert Vietnam 2026', 'Van Hanh Mall Stadium, TP.HCM', NOW() + INTERVAL '30 days', NOW() + INTERVAL '30 days 4 hours', NOW() + INTERVAL '30 days - 2 hours', 'UPCOMING')
ON CONFLICT DO NOTHING;

INSERT INTO ticket_tiers (id, event_id, tier_name, original_price, description)
VALUES 
    ('d1111111-1111-1111-1111-111111111111', 'e1111111-1111-1111-1111-111111111111', 'VIP Zone A', 2500000, 'Khu vực VIP sát sân khấu, tặng kèm lighstick'),
    ('d2222222-2222-2222-2222-222222222222', 'e1111111-1111-1111-1111-111111111111', 'GA Standing', 1200000, 'Khu vực đứng tự do')
ON CONFLICT DO NOTHING;
