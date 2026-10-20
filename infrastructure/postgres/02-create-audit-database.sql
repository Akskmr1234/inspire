-- ============================================================================
-- Creates the audit trail's own database.
-- Runs once, on first container start, as the postgres superuser.
-- ============================================================================
--
-- The audit trail - who created, changed or deleted which master or
-- transaction, and the values before and after - lives in a database of its
-- own, so restoring, resetting or damaging the books leaves it untouched.
--
-- The application role cannot create databases (01-create-app-role.sql makes
-- it NOCREATEDB on purpose), so the database is created here and handed to it.
-- Where it is missing the application keeps the trail in an 'audit' schema of
-- its own database instead and says so in its startup log.
--
-- Its table refuses UPDATE, DELETE and TRUNCATE from anybody, the application
-- role included; that is done by the application's migration, not here.

SELECT 'CREATE DATABASE inspire_erp_audit OWNER erp_app'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'inspire_erp_audit')
\gexec

\connect inspire_erp_audit

ALTER SCHEMA public OWNER TO erp_app;
GRANT ALL ON SCHEMA public TO erp_app;
