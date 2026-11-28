-- Create service-owned databases only. Application migrations create schemas.
-- This file runs once when the PostgreSQL volume is initialized.

SELECT 'CREATE DATABASE organizer_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'organizer_db')\gexec

SELECT 'CREATE DATABASE identity_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'identity_db')\gexec

SELECT 'CREATE DATABASE settlement_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'settlement_db')\gexec

