-- =============================================================================
-- Phase 3: Row-Level Security (RLS) Enhancement
-- Service: IdentityService
-- Description: Enable department-level RLS on identity tables and create
--              policies for tenant + department data isolation.
-- =============================================================================
--
-- NOTE: PERMISSIVE policy is used (not RESTRICTIVE). A RESTRICTIVE policy
-- without a matching PERMISSIVE policy causes PostgreSQL's default-deny to
-- block ALL rows. Since this single policy contains both TenantId AND
-- DepartmentId checks, PERMISSIVE is the correct and most performant choice.
-- =============================================================================

BEGIN;

-- 1. Enable RLS on department-isolated tables
ALTER TABLE "Identity"."identity_users" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Identity"."identity_sessions" ENABLE ROW LEVEL SECURITY;

-- 2. Create RLS policy for identity_users
--    PERMISSIVE: grants access when both TenantId AND DepartmentId match the
--    session variables set by IdentityRlsInterceptor.
CREATE POLICY department_isolation_policy ON "Identity"."identity_users"
AS PERMISSIVE
FOR ALL
USING (
    "TenantId" = current_setting('app.current_tenant_id')::uuid
    AND "DepartmentId" = current_setting('app.current_department_id')::uuid
);

-- 3. Create RLS policy for identity_sessions
CREATE POLICY department_isolation_policy ON "Identity"."identity_sessions"
AS PERMISSIVE
FOR ALL
USING (
    "TenantId" = current_setting('app.current_tenant_id')::uuid
    AND "DepartmentId" = current_setting('app.current_department_id')::uuid
);

COMMIT;
