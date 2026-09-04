# Deploying Aider ERP

Written against OneDeploy, and true of any platform that runs a container image
directly and injects environment variables into it — Railway, Fly, Render, App
Runner. Nothing here depends on `docker-compose.yml`; that file is for local
development only and does not exist at runtime.

Everything in this document has been verified by building both images and running
them against a PostgreSQL container with **only** the variables listed below.

---

## Two services

| Service | Root directory        | Dockerfile            | Container port |
| ------- | --------------------- | --------------------- | -------------- |
| API     | `.` (repository root) | `Dockerfile`          | 8080           |
| Web     | `apps/web`            | `apps/web/Dockerfile` | 80             |

> The API's Dockerfile is at the repository root on purpose: the build needs the
> root `.editorconfig`, which carries analyzer severities the backend compiles
> against with warnings promoted to errors. A Dockerfile under `backend/` could not
> reach it. **Set the API service's root directory to `.` and the web service's to
> `apps/web`** — each then builds from its own directory.

Link a **PostgreSQL 16** database to the API service. Redis is not required: it
appears in the compose file for future use, but no code reads it yet.

---

## Deploy order

Doing it in this order avoids a redeploy:

1. **Deploy the API.** Set the variables in the table below. Note its public URL,
   e.g. `https://api-erp.apps.example.com`.
2. **Deploy the web client** with `VITE_API_URL` set to that URL.
   This is a _build-time_ variable — see the warning below.
3. **Set the API's `Cors__AllowedOrigins`** to the web client's public URL and
   redeploy the API.

Steps 2 and 3 are a chicken-and-egg pair: each service needs the other's URL. The
order above resolves it with exactly one redeploy of the API.

---

## API variables

### Required

| Variable               | Value                                                                   |
| ---------------------- | ----------------------------------------------------------------------- |
| `Jwt__SigningKey`      | 32+ characters. Generate one: `openssl rand -base64 48`                 |
| `Cors__AllowedOrigins` | The web client's public origin, e.g. `https://web-erp.apps.example.com` |

Startup **fails loudly** on a missing or short signing key rather than starting and
issuing tokens nobody can verify. That is deliberate; do not work around it with a
placeholder.

`Cors__AllowedOrigins` accepts a plain comma-separated list. A trailing slash is
trimmed for you — browsers send `https://host`, never `https://host/`, and an origin
that does not match exactly is silently rejected by the browser while the API reports
itself perfectly healthy.

### Supplied by the platform — set nothing

| Variable                                                 | Used for                                      |
| -------------------------------------------------------- | --------------------------------------------- |
| `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER`, `PGPASSWORD` | The database connection                       |
| `DATABASE_URL`                                           | Fallback if the discrete variables are absent |
| `PORT`                                                   | The address the server binds                  |

**You do not need to set a connection string.** The application builds one in the
keyword form Npgsql requires — `Host=…;Port=…;Database=…;Username=…;Password=…` —
from the variables the platform injects. This is the single most common first-deploy
failure on .NET: `DATABASE_URL` is a URI, and Npgsql rejects URI syntax with an error
that mentions neither the variable nor the format.

To override it — pointing at a database the platform did not provision, say — set
`ConnectionStrings__Postgres` to a full keyword-form string. An explicit value always
wins.

> **If your platform injects `ConnectionStrings__Postgres` for you**, check what it
> put there. Some inject the same `postgres://` URI they use for `DATABASE_URL`.
> That is not keyword form, and it used to defeat the derivation above: the variable
> looked like a deliberate override, suppressed the `PG*` fallback that would have
> worked, and handed Npgsql a string it cannot parse. The application now translates
> a URI found under that name rather than obeying it, and leaves anything already in
> keyword form alone.
>
> The startup log names what it is dialling — `Connecting to host:port/database as
user`, password omitted — before the first attempt, and reports the actual failure
> on the first retry and every fifth after it. If a deployment cannot reach its
> database, that line says why.

### Optional

| Variable                                  | Default            | Notes                                                                                                                                                                                                                 |
| ----------------------------------------- | ------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Erp__Database__ApplyMigrationsOnStartup` | `true`             | Migrations run on boot, so a fresh database is usable immediately. Set `false` when migrations are a separate release step, or when running several replicas — they would otherwise race to apply the same migration. |
| `Erp__Seed__Enabled`                      | `false`            | Creates the first tenant, firm, chart of accounts and administrator. Turn it **on for the first deploy only**, then off.                                                                                              |
| `Erp__Seed__AdministratorPassword`        | —                  | Required when seeding. Use a real password; you sign in with it.                                                                                                                                                      |
| `Erp__Hosting__HttpsRedirection`          | off behind a proxy | Off automatically wherever the platform assigns `PORT`, because TLS is terminated in front of the container. Redirecting there would send the health probe to a URL the container does not serve.                     |
| `ASPNETCORE_ENVIRONMENT`                  | `Production`       | Setting `Development` also exposes Swagger at `/swagger`.                                                                                                                                                             |

---

## Web variables

| Variable       | When it applies     |
| -------------- | ------------------- |
| `VITE_API_URL` | **Build time only** |

Vite inlines `VITE_*` variables into the JavaScript bundle when the image is built.
Setting it on a running container does nothing — the value was compiled in already.
Two consequences:

- **Set it before the first deploy.** Adding it afterwards changes nothing until the
  image is rebuilt.
- **It must be the API's public HTTPS URL.** An internal hostname like
  `http://api:8080` resolves inside the platform's network but not in a user's
  browser.

Left unset, the client calls its own origin. That is correct for local development
(the dev server proxies `/api`) and for a single reverse proxy fronting both
services, and wrong for two separately-hosted services. There is deliberately no
localhost default: one would let the build succeed while shipping a bundle that tells
every user's browser to call their own machine — invisible in every log, and visible
only as an application that loads and then does nothing.

---

## Health checks

| Path            | Checks       | Use                                                                                          |
| --------------- | ------------ | -------------------------------------------------------------------------------------------- |
| `/`             | Nothing      | The platform's default probe. Returns 200 with a small JSON body.                            |
| `/health/live`  | Nothing      | Liveness. Deliberately dependency-free, so a brief database outage does not cause a restart. |
| `/health/ready` | Dependencies | Readiness. Gate traffic on this.                                                             |

The web service answers `/` with the application shell.

The API waits up to **30 seconds** for the database to accept connections before
giving up, then applies migrations. A container and its database are often started
together and PostgreSQL takes several seconds to initialise on first boot; without
that wait, an ordinary race becomes a failed deployment reporting "connection
refused" as though something were misconfigured.

If migrations ever grow long enough to threaten the platform's startup budget
(~60 seconds), run them as a separate step and set
`Erp__Database__ApplyMigrationsOnStartup=false`.

---

## First sign-in

Deploy once with seeding on:

```
Erp__Seed__Enabled=true
Erp__Seed__AdministratorPassword=<a real password>
```

That creates tenant `inspire`, firm `MAIN`, a full chart of accounts, the permission
catalogue, and an `admin` user. Sign in with company code `inspire`, user `admin`,
and the password you set — then **set `Erp__Seed__Enabled=false` and redeploy**, so a
later deploy can never re-seed an environment holding real data.

Seeding is idempotent, so leaving it on is survivable — but off is the right default
once the system holds anything you would mind losing.

---

## Things that bite

- **Editing configuration**: change variables in place and redeploy. Deleting a
  service cascade-deletes its variables, and recreating it lands you back at the
  original error with no record of what was set.
- **Custom domains** require a redeploy: proxy routing is baked into the container
  when it is created, so an existing container will not pick up a new hostname.
- **Row-level security**: the PostgreSQL policies that isolate tenants do not
  constrain a role holding `BYPASSRLS`, nor the owner of the tables unless
  `FORCE ROW LEVEL SECURITY` is set. A managed platform generally provisions the
  application role as the database owner. EF Core's query filters still scope every
  query by tenant, so isolation holds — but the database-level backstop is weaker
  than it is in the integration tests, which deliberately connect as a
  non-owner. Worth revisiting before a multi-tenant production launch.

---

## Local development

Unchanged, and unaffected by any of the above:

```bash
docker compose up -d postgres redis
dotnet run --project backend/src/ERP.Api
npm run dev --workspace @erp/web
```

The compose file sets `ConnectionStrings__Postgres` explicitly, which takes
precedence over anything derived from the environment, so the local stack behaves
exactly as it always has.
