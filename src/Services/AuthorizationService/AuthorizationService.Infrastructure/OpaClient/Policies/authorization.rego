# Base platform authorization policy (RBAC).
#
# This is the decision rule the AuthorizationService queries at
# `data.authorization.allow` (see OpaOptions.PolicyPath). It MUST evaluate to a
# decision object with the shape consumed by OpaDecisionResult:
#   { "allow": bool, "reason": string, "policy_id": string, "policy_version": string }
#
# Decision model (fail-closed, deny-by-default):
#   1. A subject holding the `platform.manage` permission is a platform
#      administrator and is allowed to perform any action.
#   2. Otherwise, the subject is allowed when the requested `action` is present
#      in its resolved effective permissions (RBAC grant match).
#   3. Everything else is denied.
#
# ABAC/quota is intentionally NOT encoded here (ADR-018 Sec.15): quota/debt is
# gated in the domain; OPA answers "is this permitted under policy" only.

package authorization

import rego.v1

policy_id := "platform-base-rbac"

policy_version := "v1"

# Deny by default.
default allow := {
	"allow": false,
	"reason": "Denied by policy: no matching grant.",
	"policy_id": "platform-base-rbac",
	"policy_version": "v1",
}

# Rule 1 - platform administrator (holds platform.manage).
allow := decision if {
	is_platform_admin
	decision := {
		"allow": true,
		"reason": "Allowed: platform administrator (platform.manage).",
		"policy_id": policy_id,
		"policy_version": policy_version,
	}
}

# Rule 2 - RBAC grant: the requested action is in the effective permissions.
allow := decision if {
	not is_platform_admin
	action_permitted
	decision := {
		"allow": true,
		"reason": "Allowed: effective permission grants the requested action.",
		"policy_id": policy_id,
		"policy_version": policy_version,
	}
}

is_platform_admin if {
	some permission in input.subject.permissions
	permission == "platform.manage"
}

is_platform_admin if {
	some permission in input.effective_permissions
	permission == "platform.manage"
}

action_permitted if {
	some permission in input.effective_permissions
	permission == input.action
}

action_permitted if {
	some permission in input.subject.permissions
	permission == input.action
}
