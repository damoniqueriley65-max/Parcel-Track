# ParcelTrack

CWEB2302 Web Development II, Common Coursework (Individual).

## Run it
1. Open `ParcelTrack.csproj` in Visual Studio.
2. Package Manager Console: `Update-Database`.
3. F5 (the **http** profile, http://localhost:5420), then open `/parcels`. Five parcels show.

## Demo accounts (made when the app starts)
| Email | Password | Role |
|---|---|---|
| admin@parceltrack.test | Pass123! | Admin |
| staff@parceltrack.test | Pass123! | Staff |

## What the starter already does
- Parcels and Branches tables (a foreign key), seeded, one migration
- Pages: `/parcels`, `/parcels/{id}`, add, edit, delete, `/branches/{id}`
- Identity with Admin and Staff roles: Staff add and edit, only the Admin deletes
- A JSON API at `/api/parcels` (GET, POST, PUT, DELETE) with a DTO

## My work

All seven parts of the brief are done. No new migration was needed (the schema is
unchanged; the fee is computed and the JWT settings live in `appsettings.json`).

- **Part A - tokens for the API.** A `Jwt` section was added to `appsettings.json`
  (Key, Issuer, Audience, 60-minute lifetime). `Program.cs` registers `JwtBearer` with
  `TokenValidationParameters` that check all four: issuer, audience, expiry and signing
  key. `POST /api/auth/login` (`Controllers/Api/AuthApiController.cs`) takes
  `{ "email", "password" }`, returns `200` + a token carrying the user's roles for right
  details, and `401` with the same message for a wrong password or an unknown email (an
  address that is not an email at all gives `400`). `[AllowAnonymous]` was removed from
  the API's POST, PUT and DELETE; both GETs stay open, POST and PUT need the
  `CanManageParcels` policy (Admin or Staff), and DELETE needs the Admin role. A write
  with no token gives `401`, never a `302`; a Staff token on DELETE gives `403`. The web
  pages keep their cookie login and work as before.

- **Part B - request logging middleware.** `Middleware/RequestLoggingMiddleware.cs` gives
  each request a short id (the first 8 characters of a new `Guid`), adds it to the
  response as the `X-Request-Id` header *before* passing the request on (headers cannot
  change once the response has started), and writes one console line on the way out, e.g.
  `[3f9c1a2b] POST /api/parcels -> 401 in 4 ms`. It is registered in `Program.cs` after
  `UseStaticFiles` (so `/css/site.css` is not logged) and before `UseRouting` and
  `UseAuthentication`, so `401` and `403` are still logged.

- **Part C - the `RequireClient` action filter.** `Filters/RequireClientAttribute.cs`
  inherits `ActionFilterAttribute` and is on the API's POST, PUT and DELETE only. Before
  the action: if there is no `X-Client` header (or it is empty) the action does not run
  and the API answers `400` with
  `{ "message": "Say which program is calling: add an X-Client header" }`; otherwise it
  writes a console line, e.g. `X-Client: Branch desk -> Create`. After the action it
  writes `Create finished`. Because the token check (an authorization filter) runs first,
  a request with no token still gets `401`.

- **Part D - the delivery fee service.** `Services/IDeliveryFeeCalculator.cs` and
  `Services/DeliveryFeeCalculator.cs`. The rule is J$500 base, plus J$150 per whole kg
  (the weight rounded up with `Math.Ceiling`), plus J$300 when the branch is not in
  Kingston or St Andrew. It is registered in `Program.cs` and injected into the
  constructors of both `ParcelsController` and `ParcelsApiController` (neither writes
  `new DeliveryFeeCalculator()`). `/parcels/{id}` shows the fee (e.g.
  "Delivery fee: J$1,250.00") and `ParcelDto` has a `deliveryFee` in every API reply. On
  the seeded data the fees are **800, 650, 1250, 2000 and 1250** for parcels 1 to 5.
  - **Lifetime chosen: Scoped.** The calculator itself holds no state, but it shares the
    request's scope and is used by the scoped `AppDbContext`-backed controllers, so
    Scoped keeps it consistent with everything else in the request. (Transient would also
    work for this stateless calculator; Singleton would too, but Scoped matches the
    per-request data access pattern and is the safe default for anything that may touch a
    scoped service later.)

- **Part E - the `TrackingCode` validation attribute.**
  `Validation/TrackingCodeAttribute.cs` inherits `ValidationAttribute`. A code passes when
  it is `PT-` followed by exactly 6 digits; its message is
  "Tracking code must look like PT-123456". An empty value passes (that is `[Required]`'s
  job). It is on `TrackingCode` in both `Parcel` (the forms) and `ParcelInput` (the API),
  so the Add form with `PT-12` comes back with the message and saves nothing, and the API
  gives `400` with the same message.

- **Part F - the `.http` file.** `ParcelTrack.http` has an `@host` line and two token
  lines (`@staff_token`, `@admin_token`), and the ten requests in order, each with a
  `###` line that says what it expects. Writes that should pass send an `X-Client` header.

- **Part G - it runs on the marker's PC.** The project still targets `net8.0`
  (`RollForward` lets it run on a newer .NET too), builds with no errors, needs no new
  migration, and starts on a fresh database after `Update-Database` with both demo
  accounts able to log in. The code is in a public GitHub repository.

## Anything that doesn't work
Nothing known. The fee values, the status codes for requests 1-10, the `X-Request-Id`
header on every reply and the console lines for `X-Client` all behave as the brief asks.
