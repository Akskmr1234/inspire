-- ============================================================================
-- Creates the role the application connects as.
-- Runs once, on first container start, as the postgres superuser.
-- ============================================================================
--
-- This role is deliberately NOT a superuser and deliberately does NOT hold
-- BYPASSRLS. That is the whole point of the file.
--
-- PostgreSQL exempts superusers and any role with BYPASSRLS from row-level
-- security entirely - FORCE ROW LEVEL SECURITY does not bind them. Point the
-- application at a superuser connection string and every tenant-isolation
-- policy in the database silently stops applying: no error, no warning, no
-- visible change, until one customer sees another customer's books.
--
-- ERP.Infrastructure.Tests.SchemaTests asserts current_user is neither, so this
-- cannot regress unnoticed.
--
-- The role DOES own the schema, so EF Core migrations can create and alter
-- tables. Ownership is safe precisely because the policies are FORCEd.

DO
$$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_app') THEN
        CREATE ROLE erp_app
            LOGIN
            PASSWORD 'erp_local_dev_only'
            NOSUPERUSER
            NOCREATEDB
            NOCREATEROLE
            NOBYPASSRLS
            NOREPLICATION;
    END IF;
END
$$;

-- Let the application create its own schema objects via migrations.
GRANT ALL ON DATABASE inspire_erp TO erp_app;

\connect inspire_erp

-- Own the public schema so migrations can CREATE TABLE without further grants.
ALTER SCHEMA public OWNER TO erp_app;
GRANT ALL ON SCHEMA public TO erp_app;

-- Anything created later by erp_app is owned by erp_app, so no default-privilege
-- juggling is needed as the schema grows.
ALTER DEFAULT PRIVILEGES FOR ROLE erp_app IN SCHEMA public
    GRANT ALL ON TABLES TO erp_app;
ALTER DEFAULT PRIVILEGES FOR ROLE erp_app IN SCHEMA public
    GRANT ALL ON SEQUENCES TO erp_app;
