# Two-step sign-in (privacyIDEA)

Users can protect their account with a second step after the password:

| Option                | What the user does                                             | Needs                             |
| --------------------- | -------------------------------------------------------------- | --------------------------------- |
| **Approve on phone**  | Taps _Approve_ on a notification-style request in the app      | privacyIDEA reachable from phones |
| **Authenticator app** | Types the six-digit code from any authenticator app            | Nothing extra                     |
| **Backup codes**      | Types one of ten single-use codes saved when the phone is lost | Nothing extra                     |

All of it is free. The second-factor server is [privacyIDEA](https://www.privacyidea.org)
(open source, AGPL), which the ERP runs as its own container. Phones use the free
**privacyIDEA Authenticator** app (Android and iOS) for approval; any authenticator
app — privacyIDEA Authenticator, Google Authenticator, Microsoft Authenticator,
Aegis, 2FAS — works for codes. Push runs in privacyIDEA's _poll_ mode: the app asks
privacyIDEA for pending requests, so no Firebase project, Google account or paid
push service is involved.

**It is off until you configure it.** With no `Erp__Mfa__PrivacyIdea__*` settings
the ERP signs in with a password alone, exactly as before, and the profile's
Security tab says two-step sign-in is not offered.

---

## How it fits together

```
 browser ──► ERP API ──► privacyIDEA            (enrol, check codes, send approvals)
                            ▲
 phone (privacyIDEA app) ───┘  /ttype/push      (register, collect and answer approvals)
```

- privacyIDEA holds every secret. The ERP stores only each factor's serial number
  in `user_mfa_factors`, so a copy of the ERP database cannot pass a second-factor
  check.
- After the password is right, the API answers with a short-lived, signed ticket
  (five minutes) and the user's choices instead of tokens. Tokens are issued only
  once privacyIDEA accepts the second step. Wrong codes count towards the normal
  account lockout.
- The ERP creates the push policies it needs in privacyIDEA itself, the first time
  push is used. Nothing has to be clicked in privacyIDEA's own interface.
- An administrator who loses every factor is rescued by another administrator:
  **Users → Reset 2FA** removes all of that user's factors, and they sign in with
  the password alone until they enrol again.

---

## Running privacyIDEA

The image is built from `infrastructure/privacyidea` — privacyIDEA 3.14 from PyPI,
the version the ERP was tested against. On start it creates its database (if
missing), its tables and the admin account. Restarts and image upgrades are safe:
existing tables and the admin are kept.

It runs one of two ways:

- **Without a volume (recommended on OneDeploy).** Set `PI_ENCKEY` and point
  `PI_DATABASE_URI` at PostgreSQL — the ERP's own PostgreSQL server is fine.
  Everything privacyIDEA keeps is then in that database, and the container can be
  rebuilt or replaced freely.
- **With a volume on `/data`.** Leave `PI_ENCKEY` unset. The encryption key, audit
  keys and (by default) a SQLite database are created in `/data` once. Losing
  `/data` then loses every enrolled factor.

### Its variables

| Variable            | Required         | Notes                                                                                                                                                        |
| ------------------- | ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `PI_ADMIN_PASSWORD` | yes              | The privacyIDEA admin's password. The ERP API signs in with it.                                                                                              |
| `PI_SECRET_KEY`     | yes              | `openssl rand -hex 32`. Generate once and keep.                                                                                                              |
| `PI_PEPPER`         | yes              | `openssl rand -hex 32`. **Never change it** once users have enrolled.                                                                                        |
| `PI_ENCKEY`         | without a volume | `openssl rand -base64 96`. Encrypts every factor's secret in the database. **Never change or lose it**: without it no enrolled factor can be read.           |
| `PI_DATABASE_URI`   | without a volume | `postgresql://user:password@host:5432/privacyidea`. `postgres://` URLs work too; add `?sslmode=require` if the server needs SSL. Default: SQLite in `/data`. |
| `PI_ADMIN_USER`     | no               | Default `admin`.                                                                                                                                             |
| `PI_WORKERS`        | no               | Default `2`.                                                                                                                                                 |

**Database.** Use a database of its own (`privacyidea` above), never the ERP's
database — the two must not share tables. If the named database does not exist it
is created on start; if the user may not create databases, create it once by hand
(`CREATE DATABASE privacyidea;`) and start again.

> Keep `PI_ENCKEY`, `PI_PEPPER` and `PI_SECRET_KEY` with your other secrets, and
> back up the `privacyidea` database with the ERP's. Losing any of them means every
> user needs **Reset 2FA**. Without a volume the audit log inside privacyIDEA is not
> signed; everything else is the same.

### On OneDeploy or a similar platform

Add a third service beside the API and web services. No volume is needed:

| Service     | Root directory               | Dockerfile   | Container port | Health check    |
| ----------- | ---------------------------- | ------------ | -------------- | --------------- |
| privacyIDEA | `infrastructure/privacyidea` | `Dockerfile` | 8080           | `/healthcheck/` |

Set `PI_ADMIN_PASSWORD`, `PI_SECRET_KEY`, `PI_PEPPER`, `PI_ENCKEY` and
`PI_DATABASE_URI` (same PostgreSQL server as the API, database `privacyidea`).
Give it a public HTTPS hostname if you want **Approve on phone** (see below);
codes and backup codes work over a private address alone.

### With Docker Compose

```bash
# .env beside the compose files (never commit it)
PI_ADMIN_PASSWORD=...
PI_SECRET_KEY=...
PI_PEPPER=...

docker compose -f docker-compose.yml -f docker-compose.privacyidea.yml up -d privacyidea
```

`docker-compose.privacyidea.yml` is separate on purpose: `docker-compose.yml` is
unchanged, and nothing starts unless you ask for it. The service listens on
`127.0.0.1:5100` on the host and is `http://privacyidea:8080` inside the compose
network.

---

## Turning it on in the ERP API

Set these on the **API** service:

| Variable                                     | Example                              | Notes                                                                                                                        |
| -------------------------------------------- | ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------- |
| `Erp__Mfa__PrivacyIdea__Enabled`             | `true`                               | Off by default.                                                                                                              |
| `Erp__Mfa__PrivacyIdea__BaseUrl`             | `http://privacyidea:8080`            | How the **API** reaches privacyIDEA — the internal address is best.                                                          |
| `Erp__Mfa__PrivacyIdea__AdminUser`           | `admin`                              | Same as `PI_ADMIN_USER`.                                                                                                     |
| `Erp__Mfa__PrivacyIdea__AdminPassword`       | —                                    | Same as `PI_ADMIN_PASSWORD`. Store it as a secret.                                                                           |
| `Erp__Mfa__PrivacyIdea__PushRegistrationUrl` | `https://2fa.example.com/ttype/push` | How **phones** reach privacyIDEA: public, HTTPS, ending in `/ttype/push`. Leave empty and _Approve on phone_ is not offered. |
| `Erp__Mfa__PrivacyIdea__PushVerifySsl`       | `true`                               | Keep `true` in production. `false` only for a self-signed test setup.                                                        |
| `Erp__Mfa__PrivacyIdea__Issuer`              | `Inspire ERP`                        | The name shown in authenticator apps and on approval requests.                                                               |
| `Erp__Mfa__PrivacyIdea__BackupCodeCount`     | `10`                                 | Codes per set.                                                                                                               |

Redeploy the API. The profile's **Security** tab then offers the options.

### Why push needs a public address

The phone, not the ERP, talks to privacyIDEA: it registers there when the QR code is
scanned and checks there for approval requests. So `PushRegistrationUrl` must be
reachable from the internet over HTTPS with a valid certificate. That address also
serves privacyIDEA's own admin interface — use a strong `PI_ADMIN_PASSWORD`, or, if
your proxy allows it, publish only the `/ttype/push` path.

---

## Using it

**Turning it on** — Profile → Security → _Two-step sign-in_:

- _Authenticator app_: scan the QR code (or type the key shown under _Can't
  scan?_), enter the current code, **Turn on**.
- _Approve on phone_: install **privacyIDEA Authenticator**, tap _+_ and scan the
  QR code. The page notices on its own when the phone has registered.
- _Backup codes_: copy or download them, then **I have saved them**. Making a new
  set retires the old one.

Adding backup codes alongside a phone option is strongly recommended.

**Signing in** — after the password, approval is sent to the phone at once if the
user has it; tabs switch to a code or a backup code. An approval request lasts two
minutes; _Send again_ sends a fresh one.

---

## Checked

Verified against the container built from `infrastructure/privacyidea` (with the
legacy, non-BuildKit builder too), with the API pointed at it: enrolling each kind (a wrong first authenticator code is
refused), sign-in asking for the second step, a wrong code refused and a fresh code
accepted, a replayed code refused, push approved from a simulated privacyIDEA
Authenticator (signed registration, poll and answer — the same protocol the app
uses), a backup code accepted once and refused the second time, the container
restarting with every key and factor intact, and — without a volume, on
PostgreSQL — the container destroyed and replaced with an enrolled authenticator
still accepted.
