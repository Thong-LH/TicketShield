-- ============================================================================
-- TicketShield: Clean Marketplace Seed Script
-- Purpose:
--   1. Clean out legacy 50,000 VND / mock test tickets
--   2. Update placeholder escrow records to realistic VIP values (2,500,000 VND)
--   3. Insert clean, realistic concert tickets across major Vietnamese live events
-- ============================================================================

BEGIN;

-- 1. Update placeholder escrow listings & transactions to realistic 2.500.000 VNĐ FIRST
UPDATE resale_listings
SET original_price = 2500000, resale_price = 2500000, updated_at = NOW()
WHERE id IN ('cc111111-1111-1111-1111-111111111111', 'cc222222-2222-2222-2222-222222222222');

UPDATE tickets
SET original_price = 2500000, updated_at = NOW()
WHERE id IN ('cc111111-1111-1111-1111-888888888888', 'cc222222-2222-2222-2222-888888888888');

UPDATE escrow_transactions
SET original_ticket_price = 2500000,
    buyer_fee = 125000,
    seller_fee = 75000,
    total_buyer_paid = 2625000,
    net_seller_payout = 2425000
WHERE id IN ('eeeeeeee-eeee-eeee-eeee-eeeeeeeeee01', 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeee02');

-- 2. Remove all current Verified test listings from resale_listings (excluding escrow placeholders)
DELETE FROM resale_listings 
WHERE id NOT IN ('cc111111-1111-1111-1111-111111111111', 'cc222222-2222-2222-2222-222222222222')
  AND (
    listing_status = 'Verified' 
    OR id = '44444444-4444-4444-4444-444444444444' 
    OR resale_price <= 100000
  );

-- 3. Remove unreferenced mock tickets (preserve placeholder seller escrow tickets)
DELETE FROM tickets 
WHERE id NOT IN (
    'cc111111-1111-1111-1111-888888888888',
    'cc222222-2222-2222-2222-888888888888'
) AND (
    ticket_code LIKE 'ATSH-%' 
    OR ticket_code LIKE 'TA-%' 
    OR ticket_code LIKE 'RAVO-%' 
    OR ticket_code LIKE 'WL-%' 
    OR ticket_code LIKE 'SS-%'
    OR original_price <= 100000
);

-- 4. Insert Realistic Tickets
INSERT INTO tickets (id, event_id, tier_id, current_owner_id, ticket_code, seat_zone, original_price, status, created_at, updated_at)
VALUES
    -- Anh Trai Say Hi: VIP Zone A (Row 1 Seat 15)
    ('b1000000-0000-0000-0000-000000000001', 'e1111111-1111-1111-1111-111111111111', 'd1111111-1111-1111-1111-111111111111', '11111111-1111-1111-1111-111111111111', 'ATSH-VIP-1015', 'VIP Zone A - Row 1 Seat 15', 2500000, 'LockedForResale', NOW() - interval '2 hours', NOW()),
    -- Anh Trai Say Hi: GA Standing Zone 1
    ('b1000000-0000-0000-0000-000000000002', 'e1111111-1111-1111-1111-111111111111', 'd2222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'ATSH-GA-1024', 'GA Standing Zone 1', 1200000, 'LockedForResale', NOW() - interval '2 hours', NOW()),
    -- Anh Trai Say Hi: Combo 2 Vé VIP Zone A (Row 2 Seat 10 & 11)
    ('b1000000-0000-0000-0000-000000000003', 'e1111111-1111-1111-1111-111111111111', 'd1111111-1111-1111-1111-111111111111', '75b61d33-a69e-43ea-835e-d30ac75363f7', 'ATSH-VIP-2010', 'VIP Zone A - Row 2 Seat 10', 2500000, 'LockedForResale', NOW() - interval '1 hour', NOW()),
    ('b1000000-0000-0000-0000-000000000004', 'e1111111-1111-1111-1111-111111111111', 'd1111111-1111-1111-1111-111111111111', '75b61d33-a69e-43ea-835e-d30ac75363f7', 'ATSH-VIP-2011', 'VIP Zone A - Row 2 Seat 11', 2500000, 'LockedForResale', NOW() - interval '1 hour', NOW()),

    -- Tri Âm Live Concert - Mỹ Tâm: Khán đài A (Cửa 3 Hàng 5 Ghế 18)
    ('b2000000-0000-0000-0000-000000000001', 'e2222222-2222-2222-2222-222222222222', 'd3333333-3333-3333-3333-333333333333', '11111111-1111-1111-1111-111111111111', 'TA-KDA-3518', 'Khán đài A - Cửa 3 Hàng 5 Ghế 18', 1500000, 'LockedForResale', NOW() - interval '3 hours', NOW()),
    -- Tri Âm Live Concert - Mỹ Tâm: Combo 2 Vé Khán đài A (Cửa 3 Hàng 8 Ghế 24 & 25)
    ('b2000000-0000-0000-0000-000000000002', 'e2222222-2222-2222-2222-222222222222', 'd3333333-3333-3333-3333-333333333333', '75b61d33-a69e-43ea-835e-d30ac75363f7', 'TA-KDA-3824', 'Khán đài A - Cửa 3 Hàng 8 Ghế 24', 1500000, 'LockedForResale', NOW() - interval '2 hours', NOW()),
    ('b2000000-0000-0000-0000-000000000003', 'e2222222-2222-2222-2222-222222222222', 'd3333333-3333-3333-3333-333333333333', '75b61d33-a69e-43ea-835e-d30ac75363f7', 'TA-KDA-3825', 'Khán đài A - Cửa 3 Hàng 8 Ghế 25', 1500000, 'LockedForResale', NOW() - interval '2 hours', NOW()),

    -- Ravolution Music Festival 2026: General Admission Early Bird
    ('b3000000-0000-0000-0000-000000000001', 'e3333333-3333-3333-3333-333333333333', 'd4444444-4444-4444-4444-444444444444', '11111111-1111-1111-1111-111111111111', 'RAVO-GA-2026', 'GA Floor - Early Bird Zone', 850000, 'LockedForResale', NOW() - interval '4 hours', NOW()),

    -- Westlife Vietnam Tour 2026: VIP Platinum (Row 2 Seat 08)
    ('b4000000-0000-0000-0000-000000000001', '817ad5c6-bfae-4da7-915f-49377b0c9b46', 'ae81ac4b-0d93-4ef4-b7b9-82d3ef51e1dc', '75b61d33-a69e-43ea-835e-d30ac75363f7', 'WL-VIP-0208', 'VIP Platinum - Row 2 Seat 08', 5000000, 'LockedForResale', NOW() - interval '5 hours', NOW()),

    -- SpaceSpeakers Mixtape Hanoi 2026: GA Floor Zone
    ('b5000000-0000-0000-0000-000000000001', '9ceb0954-04b1-40a4-8861-015536dd8a76', 'da2ffb49-208b-40d0-a380-1885ed1f20a0', '11111111-1111-1111-1111-111111111111', 'SS-GA-1102', 'GA Floor - Khu Trung Tâm', 800000, 'LockedForResale', NOW() - interval '1 hour', NOW());

-- 5. Insert Matching Resale Listings
INSERT INTO resale_listings (
    id, event_id, tier_id, seller_id, ticket_id,
    original_ticket_code, original_price, resale_price, applied_markup_percentage,
    is_private, private_access_token, verification_status, listing_status,
    bundle_id, bundle_total_tickets, is_bundle_all_or_nothing, seat_zone,
    created_at, updated_at
)
VALUES
    -- ATSH Single VIP Zone A
    (
        'a1000000-0000-0000-0000-000000000001',
        'e1111111-1111-1111-1111-111111111111',
        'd1111111-1111-1111-1111-111111111111',
        '11111111-1111-1111-1111-111111111111',
        'b1000000-0000-0000-0000-000000000001',
        'ATSH-VIP-1015',
        2500000,
        2650000,
        6.00,
        false,
        null,
        'Verified',
        'Verified',
        null,
        1,
        false,
        'VIP Zone A - Row 1 Seat 15',
        NOW() - interval '2 hours',
        NOW()
    ),
    -- ATSH Single GA Standing Zone 1
    (
        'a1000000-0000-0000-0000-000000000002',
        'e1111111-1111-1111-1111-111111111111',
        'd2222222-2222-2222-2222-222222222222',
        '11111111-1111-1111-1111-111111111111',
        'b1000000-0000-0000-0000-000000000002',
        'ATSH-GA-1024',
        1200000,
        1150000,
        0.00,
        false,
        null,
        'Verified',
        'Verified',
        null,
        1,
        false,
        'GA Standing Zone 1',
        NOW() - interval '2 hours',
        NOW()
    ),
    -- ATSH Combo 2 Vé VIP Zone A (Row 2 Seat 10 & 11)
    (
        'a1000000-0000-0000-0000-000000000003',
        'e1111111-1111-1111-1111-111111111111',
        'd1111111-1111-1111-1111-111111111111',
        '75b61d33-a69e-43ea-835e-d30ac75363f7',
        'b1000000-0000-0000-0000-000000000003',
        'ATSH-VIP-2010',
        2500000,
        2500000,
        0.00,
        false,
        null,
        'Verified',
        'Verified',
        'c1000000-0000-0000-0000-000000000001',
        2,
        true,
        'VIP Zone A - Row 2 Seat 10',
        NOW() - interval '1 hour',
        NOW()
    ),
    (
        'a1000000-0000-0000-0000-000000000004',
        'e1111111-1111-1111-1111-111111111111',
        'd1111111-1111-1111-1111-111111111111',
        '75b61d33-a69e-43ea-835e-d30ac75363f7',
        'b1000000-0000-0000-0000-000000000004',
        'ATSH-VIP-2011',
        2500000,
        2500000,
        0.00,
        false,
        null,
        'Verified',
        'Verified',
        'c1000000-0000-0000-0000-000000000001',
        2,
        true,
        'VIP Zone A - Row 2 Seat 11',
        NOW() - interval '1 hour',
        NOW()
    ),
    -- Tri Âm Mỹ Tâm Single Khán đài A
    (
        'a2000000-0000-0000-0000-000000000001',
        'e2222222-2222-2222-2222-222222222222',
        'd3333333-3333-3333-3333-333333333333',
        '11111111-1111-1111-1111-111111111111',
        'b2000000-0000-0000-0000-000000000001',
        'TA-KDA-3518',
        1500000,
        1450000,
        0.00,
        false,
        null,
        'Verified',
        'Verified',
        null,
        1,
        false,
        'Khán đài A - Cửa 3 Hàng 5 Ghế 18',
        NOW() - interval '3 hours',
        NOW()
    ),
    -- Tri Âm Mỹ Tâm Combo 2 Vé Khán đài A (Cửa 3 Hàng 8 Ghế 24 & 25)
    (
        'a2000000-0000-0000-0000-000000000002',
        'e2222222-2222-2222-2222-222222222222',
        'd3333333-3333-3333-3333-333333333333',
        '75b61d33-a69e-43ea-835e-d30ac75363f7',
        'b2000000-0000-0000-0000-000000000002',
        'TA-KDA-3824',
        1500000,
        1400000,
        0.00,
        false,
        null,
        'Verified',
        'Verified',
        'c2000000-0000-0000-0000-000000000001',
        2,
        true,
        'Khán đài A - Cửa 3 Hàng 8 Ghế 24',
        NOW() - interval '2 hours',
        NOW()
    ),
    (
        'a2000000-0000-0000-0000-000000000003',
        'e2222222-2222-2222-2222-222222222222',
        'd3333333-3333-3333-3333-333333333333',
        '75b61d33-a69e-43ea-835e-d30ac75363f7',
        'b2000000-0000-0000-0000-000000000003',
        'TA-KDA-3825',
        1500000,
        1400000,
        0.00,
        false,
        null,
        'Verified',
        'Verified',
        'c2000000-0000-0000-0000-000000000001',
        2,
        true,
        'Khán đài A - Cửa 3 Hàng 8 Ghế 25',
        NOW() - interval '2 hours',
        NOW()
    ),
    -- Ravolution Single GA Early Bird
    (
        'a3000000-0000-0000-0000-000000000001',
        'e3333333-3333-3333-3333-333333333333',
        'd4444444-4444-4444-4444-444444444444',
        '11111111-1111-1111-1111-111111111111',
        'b3000000-0000-0000-0000-000000000001',
        'RAVO-GA-2026',
        850000,
        800000,
        0.00,
        false,
        null,
        'Verified',
        'Verified',
        null,
        1,
        false,
        'GA Floor - Early Bird Zone',
        NOW() - interval '4 hours',
        NOW()
    ),
    -- Westlife Single VIP Platinum
    (
        'a4000000-0000-0000-0000-000000000001',
        '817ad5c6-bfae-4da7-915f-49377b0c9b46',
        'ae81ac4b-0d93-4ef4-b7b9-82d3ef51e1dc',
        '75b61d33-a69e-43ea-835e-d30ac75363f7',
        'b4000000-0000-0000-0000-000000000001',
        'WL-VIP-0208',
        5000000,
        5000000,
        0.00,
        false,
        null,
        'Verified',
        'Verified',
        null,
        1,
        false,
        'VIP Platinum - Row 2 Seat 08',
        NOW() - interval '5 hours',
        NOW()
    ),
    -- SpaceSpeakers Single GA Floor Zone
    (
        'a5000000-0000-0000-0000-000000000001',
        '9ceb0954-04b1-40a4-8861-015536dd8a76',
        'da2ffb49-208b-40d0-a380-1885ed1f20a0',
        '11111111-1111-1111-1111-111111111111',
        'b5000000-0000-0000-0000-000000000001',
        'SS-GA-1102',
        800000,
        750000,
        0.00,
        false,
        null,
        'Verified',
        'Verified',
        null,
        1,
        false,
        'GA Floor - Khu Trung Tâm',
        NOW() - interval '1 hour',
        NOW()
    );

COMMIT;
