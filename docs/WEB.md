# The web application

How `apps/web` actually works: what happens between a user opening the page and a
trial balance appearing on it, and where to make a change so it lands in the right
place.

Written against the code as it stands. Where a decision looks odd, the reason it is
that way is given — those are the parts most likely to be "corrected" back into a
bug.

---

## Shape

A single-page React application. No server rendering, no server components: it is
built to a set of static files and served by nginx, and it talks to the .NET API
over `/api/v1`.

| Concern            | Where                              |
| ------------------ | ---------------------------------- |
| Routing and gating | `src/App.tsx`                      |
| Chrome and layout  | `src/components/AppShell.tsx`      |
| Screens            | `src/pages/` — 29 files, 33 routes |
| Shared UI          | `src/components/`                  |
| API clients        | `src/lib/` — one module per domain |
| Session and theme  | `src/stores/session.ts`            |
| Design tokens      | `src/index.css`                    |
| Translations       | `src/i18n/index.ts`                |

Four dependencies carry the weight: **React Router** for navigation, **TanStack
Query** for server state, **Zustand** for the session, and **Tailwind v4** for
styling. There is no client-side state manager beyond the session — everything else
a screen shows comes from a query, which is deliberate: an ERP's data belongs to the
server, and a second copy of it in a store is a second thing to be wrong.

---

## Booting

1. `index.html` runs a small inline script **before the first paint**. It reads the
   stored theme and language and stamps `class="dark"`, `lang` and `dir` onto
   `<html>`. This exists to stop the flash: the session store applies the same
   values, but only after React mounts, which is a frame too late and shows as a
   white page repainting dark.
2. `main.tsx` mounts `<App/>` inside a `QueryClientProvider` and `BrowserRouter`.
3. `App` reads `status` from the session store. It starts as `unknown`.
4. While `unknown`, a spinner shows and `restore()` runs.

### The query client

Configured once in `main.tsx`, and two of its settings are load-bearing:

- **`staleTime: 0`.** Financial figures must not be quietly stale. An accountant
  refreshing a trial balance expects the current position, not a cached one from ten
  minutes ago.
- **`retry` skips 4xx.** The API has already refused the request on its merits;
  repeating it three times only delays the error. The client's own token-refresh path
  already handles the one 401 worth retrying.

---

## Signing in, and staying signed in

Token handling is split on purpose, and the split is a security decision rather than
an accident:

- **The access token lives in memory only** (`let accessToken` in `lib/api.ts`). It is
  never written to storage, so a cross-site scripting flaw cannot read it out, and it
  disappears when the tab closes.
- **The refresh token lives in `localStorage`** under `erp.refreshToken`, because a
  signed-in user reasonably expects to survive a reload. That is a real trade-off, not
  an oversight — an httpOnly cookie would be stronger, but the API does not set
  cookies. The mitigation that does exist is refresh-token rotation with family
  revocation: presenting a token twice revokes the whole session family, because reuse
  cannot be distinguished from theft.

```
LoginPage ──► login(user, password, tenant)
                  │  POST /auth/login
                  ▼
            setSession(auth, tenantCode)
                  │  access token → memory
                  │  refresh token → localStorage
                  ▼
            GET /auth/permissions ──► Set<string> in the store
                  │
                  ▼
            status: 'signedIn'  ──►  <AppShell/> renders
```

On reload, `restore()` exchanges the stored refresh token instead. If there is no
token, or the exchange fails, the status becomes `signedOut` and the router serves
only `/login`.

### Concurrent expiry

Three queries expiring together would each present the same refresh token, and the
server — correctly unable to tell reuse from theft — would revoke the entire session
and sign the user out. `refreshSession()` therefore shares one in-flight promise
between all callers.

### Permissions

`can(code)` answers whether the user holds a `module:resource:verb` permission. A
super administrator's list is `["*"]`, not several hundred codes, so the check tests
for the wildcard first — a naïve set-membership test would report false for every
specific permission and hide the entire interface from the most privileged user.

**This is courtesy, never a boundary.** Every endpoint checks for itself. Hiding a
button the server would refuse is politeness; relying on the hiding to enforce the
rule would mean anyone with the developer tools could bypass it. The same applies to
`requiredPermission` on a grid column: the row has already been sent to the browser,
so a genuinely sensitive field must be left out of the _response_, not merely out of
the table.

---

## Routing

`App.tsx` declares 33 screens plus a catch-all. Everything except the sign-in page is
loaded on demand.

```tsx
const TrialBalancePage = named(
  () => import('@/pages/TrialBalancePage'),
  'TrialBalancePage',
);
```

`named()` exists because `React.lazy` understands default exports only, and several
modules deliberately export more than one screen — the five stock reports share their
column definitions, and brands and categories are the same master with a different
noun. Requiring one default export per screen would mean splitting files that belong
together.

Loaded eagerly the application is a single ~570 kB script, so opening the trial
balance would download the product editor, the stock ledger and thirty others first.
That is paid for on the slowest connection by the person least able to afford it, and
paid again on every deployment, because one changed line invalidates the whole bundle.
Split, the entry chunk is ~332 kB across 44 chunks.

`AppShell` and `LoginPage` stay eager: they are what the first paint needs in either
state, and deferring them would only add a spinner before the spinner.

A `<Suspense>` fallback shows a heading skeleton and a table skeleton, because nearly
every route under it is a table.

---

## The shell

`AppShell` is the navigation, the header, and an outlet.

The navigation is **one component in two modes, not two components**. On a wide screen
it is a column that can narrow to icons; below `lg` the same markup becomes a drawer
over a backdrop, because a 240 px column on a 390 px phone leaves no room for the
ledger it exists to open. Writing it twice would mean every menu change had to be made
in both, and one of them would drift.

The menu is **data, not code**: it is fetched from `/menu` once a session begins. The
server has already removed everything the user cannot reach, and every heading left
empty by that filtering, so the client renders what it is given and decides nothing.
Entries form a tree of arbitrary depth — an administrator can create their own groups,
so two levels is a guess rather than a limit.

### The drawer

Mounted only while open, which is what lets `useModalBehaviour` treat its own mount and
unmount as the drawer opening and closing. That hook provides the four things that
separate a dialog from a div drawn on top of the page:

| Behaviour              | Why it is not optional                                                  |
| ---------------------- | ----------------------------------------------------------------------- |
| Focus moves in         | Otherwise the first Tab walks the page _underneath_ the overlay         |
| Tab cycles within      | Otherwise focus escapes and there is no way back without a mouse        |
| Focus returns on close | Otherwise every closed dialog dumps the user at the top of the document |
| Escape closes          | The other half of the contract                                          |

It also locks `body` scroll while open and releases it on unmount.

Visibility is tested with `checkVisibility()`, **not** `offsetParent`. That property is
null for hidden elements but also for anything positioned `fixed`, so a dialog with a
pinned toolbar would have silently dropped it out of the focus cycle.

### The skip link

First in tab order, parked off the top edge with a transform and slid in on focus.

It uses a transform rather than `sr-only` + `focus:not-sr-only` on purpose. That pairing
works only while Tailwind happens to emit its `position` utility _after_ `not-sr-only`:
the two rules have identical specificity, so source order decides, and if it ever flips
the link reverts to `position: static` and shoves the whole layout down the moment it
takes focus.

---

## Fetching data

Every screen follows the same path:

```
Page ──► useQuery({ queryKey, queryFn })
              │
              ▼
         lib/<domain>.ts  ──► request<T>('/path?query')
              │
              ▼
         lib/api.ts       ──► fetch('/api/v1' + path)
                               ├─ attaches the bearer token
                               ├─ on 401, refreshes once and retries
                               └─ on !ok, throws ApiError(status, code, detail)
```

`ApiError` carries the API's RFC 9457 problem details. **Branch on `code`, not on
`message`** — the code is stable, the message is prose.

Query keys include every filter the screen applies, so changing a date range is a new
key and therefore a new fetch rather than a manual invalidation.

### Where the API lives

`VITE_API_URL` is inlined **at build time**, so it is fixed the moment the image is
produced and cannot be changed by setting an environment variable on the running
container. Deploying the UI and API as separate services therefore means supplying the
API's public origin as a build argument.

Left unset it falls back to a same-origin relative path, which is what local
development wants (Vite proxies `/api`) and what a single-origin deployment behind one
reverse proxy wants. There is deliberately **no localhost default**: a default that
happens to work on a developer's machine ships a bundle pointing at the user's own
computer, which fails only in the browser and leaves nothing in any log.

---

## Screens

Three frames cover almost every screen. Reach for one before writing markup.

### `ReportFrame`

The chrome every report shares: heading, controls, and the loading, error and empty
states. It exists because repeating that handling per screen is how one of them ends up
silently rendering a blank page on failure.

| State                 | What is shown                                        |
| --------------------- | ---------------------------------------------------- |
| Pending               | A skeleton shaped like the table that is coming      |
| Error                 | The `code`, the `detail`, and a retry that refetches |
| Empty (via `isEmpty`) | An empty state saying the query matched nothing      |
| Refetching over data  | A hairline, **not** a spinner                        |

That last row matters: blanking a statement somebody is reading in order to say
"loading" costs them their place.

It also renders the print button, since printing is what half these screens are for.

### `DataGrid`

The list component. Sorting, searching and column arrangement happen in the browser,
which is right for a few hundred rows and makes typing in the search box instant rather
than a round trip per keystroke.

A list that outgrows the browser passes `paging`. **In that mode sorting and the search
box are withdrawn** rather than left working on the page in hand: a search that quietly
looked at fifty rows out of four thousand would answer "no such invoice" about an
invoice that exists, which is worse than not offering the box. The screen owns the
filters instead, because only the server can apply them to the whole list.

A column separates `value` from `render` because sorting, searching and export all need
the value and none of them can do anything useful with a React element — a column
rendering a coloured badge still has to sort and export as the word behind it.

**Below `sm` the grid stops being a table** and becomes a list of labelled cards. One or
the other is rendered, not both hidden behind a breakpoint class, so a four-thousand-row
chart of accounts does not build twice. Because the cards have no headers, sorting moves
into the toolbar as a select, and the freeze control is withdrawn — freezing a column
means nothing once the columns are gone.

Layouts (order, hidden columns, sort, freeze) persist per user per `gridKey` via
`/grid-layouts/{key}`. A saved layout is **reconciled rather than trusted**: unknown keys
are dropped and new columns appear at the end, so a release that adds a column does not
leave it invisible to everybody who had already arranged the grid.

CSV export quotes anything holding a delimiter, quote or newline, and writes a BOM —
without it Excel reads UTF-8 as the local code page and turns every Arabic name into
punctuation.

### `MasterFrame`

The chrome the inventory masters share: the list, the include-withdrawn toggle, the add
form, and the plumbing that refreshes one after the other. Extracted after the second of
four rather than the first — repeating the mutation and invalidation per screen is how
three of them end up refreshing and the fourth does not.

---

## Styling

Tailwind v4, configured **in CSS** (`src/index.css`) rather than a JavaScript config.

### Dark mode — read this before touching it

```css
@custom-variant dark (&:where(.dark, .dark *));
```

**This line is load-bearing.** Tailwind v4 dropped the JavaScript `darkMode` option and
its `dark:` variant now compiles to `@media (prefers-color-scheme: dark)` unless a custom
variant says otherwise. Without it, every `dark:` utility in the application follows the
operating system and ignores the theme switch — the body repaints (that rule is written
by hand) while every card, table, input and border stays light. That reads as "the theme
only works on some sections", and it is exactly the bug this codebase had.

`:where()` keeps the variant at zero specificity, so `dark:` overrides win purely on
source order.

### Semantic tokens

```
--color-surface  ──► var(--app-surface)  ──► :root sets white, :root.dark sets slate
```

`bg-surface` is correct in both themes with **no `dark:` counterpart at the call site**.
That indirection is what stops a screen being half-converted: there is no second class to
forget. Prefer these over raw palette colours.

| Token family                        | Use for                              |
| ----------------------------------- | ------------------------------------ |
| `canvas`                            | The page behind everything           |
| `surface`, `surface-2`, `surface-3` | Cards, insets, table headers         |
| `line`, `line-strong`               | Borders and rules                    |
| `ink`, `ink-muted`, `ink-subtle`    | Text, in descending prominence       |
| `brand-50…950`                      | The one indigo hue, across the scale |

Coloured states (success, warning, danger) keep their hue in dark mode as a **low-alpha
tint**, not the darkest step of the ramp. A `red-950` panel reads as a dark grey
rectangle with faint warmth, which is the wrong signal for "bounced" — the point of the
colour is that it is legible across the room.

### Component classes

`.card`, `.panel`, `.btn-*`, `.field-*`, `.badge-*`, `.alert-*`, `.table`, `.skeleton`,
`.nav-link` and friends live in `@layer components`.

Variants are written as a shared rule plus a difference rather than by `@apply`-ing one
component class inside another: **Tailwind v4 resolves `@apply` against registered
utilities only**, so a component class cannot compose a sibling. Keeping them in
`@layer components` — rather than registering them with `@utility` — is also what lets a
caller override one with a plain utility, since the utilities layer is emitted last.

### Motion

Keyframes and `--animate-*` tokens are declared in `@theme`. Motion is decoration, never
information: every animated element is already in its final position and legible with
animation switched off, and the whole system collapses under
`prefers-reduced-motion: reduce` (measured: 0.36 s → 0.001 s).

Theme changes cross-fade via a `.theme-switching` class added for 300 ms and then removed
— left permanently on every element it would also animate ordinary hover and focus
colours, and a row that takes a quarter-second to acknowledge the pointer feels broken.

---

## Responsive strategy

One breakpoint does most of the work: **`lg` (1024 px)** switches the sidebar between
column and drawer. **`sm` (640 px)** switches the data grid between table and cards.

Three rules keep it honest:

1. **Wide content scrolls inside its own container**, never the page. Every report table
   sits in `.table-wrap`; the choice is between scrolling the table or scrolling the
   page, and scrolling the table keeps the surrounding screen usable.
2. **`min-w-0` on flex and grid children.** Without it a long product description sets the
   column's minimum width and pushes the whole page into a horizontal scroll.
3. **Toolbars scroll sideways below `sm`** rather than wrapping into a four-line stack
   that pushes the report itself off the screen.

`.table-wrap-tall` adds a ceiling and pins the header. That needs the ceiling: `.table-wrap`
already sets `overflow-x`, and CSS computes the other axis to `auto` the moment one axis is
not `visible` — so the wrapper is a scroll container in both directions whether or not it
was meant to be, and a sticky `thead` inside a box the height of its content has nothing to
stick to.

---

## Arabic and RTL

Setting `dir="rtl"` on `<html>` mirrors the entire interface. That works only because the
styles use **logical properties throughout** — `ms`/`me`, `start`/`end`, `inset-inline-start`
— rather than left and right.

Two traps, both of which this codebase has hit:

- **`translateX` is a physical axis.** The language switch marker offsets with
  `inset-inline-start` rather than a translate, because a translate sends it off the wrong
  end under Arabic.
- **`transform-origin` takes physical keywords only.** A bar that unrolls from the reading
  edge needs `transform-origin` flipped by a `[dir='rtl']` rule; a keyframe cannot express
  it.

Direction-specific glyphs (`‹ ›`) must mirror too — "earlier" is leftward in English and
rightward in Arabic.

Arabic also gets its own font stack: at the same point size Arabic script reads smaller than
Latin because its letterforms carry more detail.

---

## Printing

An accounting application is printed constantly — a trial balance to the auditor, a register
to the meeting. The `@media print` block sits deliberately **outside `@layer`** so it wins
against the utilities it has to override.

It: overrides the tokens to black on white (so every component follows, including ones
written later); hides `.no-print` chrome and all buttons; lifts `max-height` and `overflow`
so nothing is clipped at a scroll container; sets `thead { display: table-header-group }` so
headings repeat on every sheet; and avoids breaking rows and cards across pages.

Date inputs are deliberately **kept** and flattened to plain text — a trial balance with no
period on it is a page of figures nobody can file.

---

## When a screen throws

`ErrorBoundary` wraps the routed outlet **inside** the shell, so a failing screen leaves the
navigation and header standing. It is keyed on the path, so navigating away clears it.

Without it React unmounts the whole tree: the navigation, the header and the route disappear
together and the user is left on a blank page with nothing to click. That is not
hypothetical — it is what this application did when a single numeric field came back absent
and a `toFixed` ran on `undefined`.

The failure modes it is for are the ones an ERP meets in the field: a server that adds or
renames a field ahead of the client, an optional figure that is null for one branch only, a
stale cached response after a deployment. In each case one screen is broken and the rest is
fine, so only the screen should break.

The message is shown rather than hidden behind "contact support" — an ERP is run by people
who will paste it into a ticket, and a specific line beats "something went wrong".

---

## Tests

`npm test --workspace @erp/web` — Vitest, jsdom, Testing Library. 69 tests, run in CI
**before** the build so a broken component fails in seconds with the assertion that caught it.

| Suite                        | Covers                                                                                                   |
| ---------------------------- | -------------------------------------------------------------------------------------------------------- |
| `DataGrid.test.tsx`          | Sorting (keyboard, `aria-sort`, by value not rendered text), permission columns, CSV escaping, card view |
| `ReportFrame.test.tsx`       | Loading, error, retry, empty, refetch-over-data, print, money formatting                                 |
| `ErrorBoundary.test.tsx`     | Containment, message, reset on route change, retry                                                       |
| `useModalBehaviour.test.tsx` | Focus in, focus restored, Escape, Tab trap, scroll lock                                                  |
| `pages.test.tsx`             | All 33 screens render their figures without reaching the boundary                                        |

`src/test/setup.ts` shims what jsdom lacks: `matchMedia` (steerable, so the card view is
reachable), `Blob` content capture (jsdom has no `Blob.prototype.text`), and anchor clicks at
blob URLs.

### Writing a page test

Two things are easy to get wrong and both make a test that always passes:

1. **Do not wait on the heading.** `ReportFrame` renders it while the query is still pending,
   so the assertion runs against a screen that has not drawn a row. Wait for the mocked
   requests to settle.
2. **Build fixtures from the backend's DTO field names**, not from memory. A fixture that
   guesses tests only the guess, and fails in a way indistinguishable from an application
   bug.

---

## Making a change

| To…                        | Do this                                                                                             |
| -------------------------- | --------------------------------------------------------------------------------------------------- |
| Add a screen               | Page in `src/pages/`, `named()` entry and `<Route>` in `App.tsx`, add it to `pages.test.tsx`        |
| Add an endpoint call       | Function in the matching `src/lib/<domain>.ts`, typed to the backend DTO                            |
| Add a colour               | A semantic token in `index.css`; only reach for a palette colour for a state that must keep its hue |
| Restyle something repeated | A class in `@layer components`, not a copied string of utilities                                    |
| Add a filter               | Into the query key, so the refetch is automatic                                                     |
| Add a translation          | Both `en` and `ar` in `src/i18n/index.ts` — a missing Arabic key falls back to English silently     |

### Before opening a pull request

```bash
npm run typecheck
npm test --workspace @erp/web
npm run build
npm run format:check
```

CI runs all four. `format:check` covers the whole repository, not just the frontend.

---

## Known limits

- **The i18n bundle is inline** in the entry chunk (~30–40 kB of 332 kB). Splitting it per
  language is worthwhile eventually; the file's own comment defers it until the surface
  warrants the indirection.
- **`checkVisibility` has no fallback** beyond including the element. Where the method is
  missing, an extra Tab stop is preferred to stranding a keyboard user.
- **Column-level search is table-only.** The card view offers the global search box; a
  per-column filter has no card equivalent yet.
