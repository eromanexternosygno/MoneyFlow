# SPEC.md — Especificación Técnica del Proyecto MoneyFlow

> Documento de referencia para consumo por IA. Describe **qué es**, **cómo está construido** y **cómo funciona** el sistema MoneyFlow. No incluye recomendaciones de mejora ni plan de remediación.

---

## 0. Cómo leer este documento

| Si te interesa… | Lee la sección |
|---|---|
| El negocio y el propósito de la aplicación | 1 |
| Arquitectura, capas, DI y pipeline | 3, 4, 5 |
| Stack y versiones | 4 |
| Modelo de datos y esquema real de BD | 9 |
| Catálogo de consultas SQL | 10 |
| Endpoints HTTP | 11 |
| Flujos de negocio paso a paso | 12 |
| Procesamiento en background | 13 |
| Capa de negocio (Managers / Utilities) | 14 |
| Front-end (Razor, JS, recursos) | 8 |
| Contratos (DTOs / ViewModels) | 15 |
| Tests | 16 |
| Glosario de dominio | 18 |

**Advertencia importante:** El esquema real de base de datos **no está completamente representado en las migraciones de EF Core**. Existen dos vías paralelas: el modelo EF (`AppDbContext`, que solo conoce `User`, `Service`, `Transaction`) y las tablas que el código usa en runtime vía Dapper/SQL directo. Ver secciones **9.2** y **10**.

Todas las referencias usan el formato `archivo:línea` para que puedas saltar al código y verificar.

---

## 1. Qué es MoneyFlow

MoneyFlow es una aplicación web interna (panel de control) construida para dar soporte a la operación de una red de estaciones de servicio que operan el sistema **GAXPOS**. Su propósito es consultar, procesar y auditar información distribuida entre una base de datos corporativa central y las bases de datos individuales de cada estación.

### 1.1 Funcionalidades

| Módulo | Descripción |
|---|---|
| **Estaciones** | Búsqueda de estaciones activas cruzando `oxxogas.dbo.EstacionesReportes` con `oxxogas.dbo.relacionestaciones`. |
| **Recibos y PO** | Consulta de recibos (`Purchase.Receipt`) por estación, obtención de detalles de órdenes de compra (`Purchase.PO`) y **actualización masiva de remisiones** con evidencia gráfica antes/después. |
| **Búsqueda masiva de folios** | Consulta de órdenes de venta (`Sale.Order`) en hasta 15 estaciones en paralelo, particionada en lotes de 1 000 folios. Resultados persistidos localmente, exportables a Excel. |
| **Transacciones procesadas** | Carga de Excel (`.xlsx`), previsualización agrupada por estación, procesamiento en background con Hangfire (lotes de 500), seguimiento de progreso por `jobId`, exportación a CSV. |
| **Auditorías de inventario** | Consulta de auditorías (`Inventory.Audit`), detalle de productos (`ProductAuditInventory`) y detalle por ubicación (`ProductAuditInventoryDetail` + `Location`). Exportación a PDF con Rotativa. |
| **Servicios y transacciones** | CRUD de servicios por usuario y registro de transacciones (ingresos/gastos) con histórico por rango de fechas. |
| **Autenticación** | Inicio de sesión por cookie con Claims. |

---

## 2. Contexto de ejecución

### 2.1 Restricciones de plataforma
- **Windows únicamente.** Rutas absolutas en `Program.cs:39` (`C:\LogsAppTransaction\log-.txt`) y `StationManager.cs:128` (`C:\AppReceiptsEvidenceStations`). Binarios `wkhtmltopdf.exe` / `wkhtmltoimage.exe` versionados en `wwwroot/Rotativa/`.
- **Runtime:** .NET 9.0. No existe `global.json`, por lo que el SDK no está pineado.
- **Desarrollo:** perfil único `http` en `Properties/launchSettings.json`, `http://localhost:5184`, `ASPNETCORE_ENVIRONMENT=Development`. Sin perfil HTTPS.
- **Sin CI/CD.** `.github/workflows/` existe pero está vacío.
- **Sin Docker, sin `.editorconfig`, sin `Directory.Build.props`, sin OpenAPI/Swagger.**

### 2.2 Topología de datos

El sistema conversa con **tres dominios de datos distintos**:

```
┌─────────────────────────────────────────────────────────────────┐
│  MoneyFlowDb   (C0DZJS3\SQLEXPRESS, Windows Integrated Security)│
│  ─ Connection string: "LocalDb"  (appsettings.json:16)           │
│  ─ Acceso: EF Core 9.0.7 + Dapper 2.1.66 + Hangfire SqlServer   │
│  ─ Contenido: User, Service, Transaction, TransaccionesProcesadas│
│                EstacionesMaestras, CorrectionHistory,            │
│                LocalBulkSearchResults, BulkSearchErrors,         │
│                HangFire.*                                        │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│  oxxogas  (10.52.21.11, SQL Auth: oxxogasusr / password en appset)│
│  ─ Connection string: "StationsDb"  (appsettings.json:17)        │
│  ─ Acceso: Dapper 2.1.66 ÚNICAMENTE (sin EF Core)                │
│  ─ Contenido: dbo.EstacionesReportes, dbo.relacionestaciones     │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│  [LS].gaxpos / [LS].GAXPOS   — Linked Servers dinámicos          │
│  ─ Acceso: Dapper con identificador de instancia interpolado     │
│  ─ Esquemas por estación:                                         │
│      gaxpos.Purchase.{Receipt, PO}   gaxpos.Catalog.RecordType   │
│      gaxpos.Sale.Order                GAXPOS.Security.{Users, Roles}│
│      GAXPOS.Inventory.{Audit, ProductAuditInventory,              │
│                        ProductAuditInventoryDetail, Location}     │
│      GAXPOS.System.Stations                                        │
│  ─ Resolución IdEstacion → LS: tabla local EstacionesMaestras    │
└─────────────────────────────────────────────────────────────────┘
```

**Implicación arquitectónica:** El acceso a datos federados se resuelve enteramente en la capa de negocio (Managers) mediante Dapper. No existe capa de Anti-Corruption, ni gateway de API, ni clientes HTTP externos. No hay ningún uso de `HttpClient` / `IHttpClientFactory` en el proyecto.

---

## 3. Arquitectura

### 3.1 Estilo arquitectónico

MoneyFlow implementa una **arquitectura por capas (Layered Architecture) sobre ASP.NET Core MVC**, con el patrón **Manager** como variante del Service Layer en la capa de negocio. Es un **monolito modular**: un único proceso desplegable, sin microservicios, sin CQRS, sin Event Sourcing, sin DDD estricto (entidades anémicas sin comportamiento de dominio).

```
┌──────────────────────────────────────────────────────────────┐
│  CAPA DE PRESENTACIÓN                                        │
│  Controllers/ (8 clases, 41 acciones)    Views/ (19 .cshtml) │
│  HTTP routing, model binding, validación, renderizado Razor, │
│  lógica de cliente (JS inline), llamadas fetch/FormData      │
└───────────────────────────┬──────────────────────────────────┘
                            │ inyecta interfaces (Scoped)
┌───────────────────────────▼──────────────────────────────────┐
│  CAPA DE APLICACIÓN / NEGOCIO  ("Managers")                  │
│  Interfaces/ (6)  Managers/ (7 managers + 1 job + 1 vacía)   │
│  Orquestación, reglas, transformación, acceso a datos,       │
│  encolado de jobs (Hangfire), generación de Excel/CSV/PDF    │
└──────────┬─────────────────────────────┬─────────────────────┘
           │                             │
┌──────────▼──────────────┐   ┌──────────▼─────────────────────┐
│  ACCESO A DATOS (EF)    │   │  ACCESO A DATOS (Dapper)       │
│  AppDbContext           │   │  SqlConnection por operación   │
│  Entities/ (5)          │   │  SQL parametrizado +           │
│  Migrations/ (3 .cs)     │   │  identificadores interpolados │
└─────────────────────────┘   └────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│  TRANSFERENCIA  DTOs/ (13)   Models/ (8)   Entities/ (5)     │
│  DTOs = contratos de lógica/API JSON  |  ViewModels = vistas │
└──────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────┐
│  UTILIDADES  Utilities/ (5)                                  │
│  CsvHelper, ExcelTransaccionesReader, HangfireAuthorization  │
│  Filter, PasswordService, UserMigrationService               │
└──────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────┐
│  COMPOSICIÓN RAÍZ  Program.cs (117 líneas)                   │
│  DI container, middleware pipeline, logging, Hangfire setup  │
└──────────────────────────────────────────────────────────────┘
```

### 3.2 Decisiones de diseño (con su razonamiento observable en el código)

| Decisión | Consecuencia observable en el código |
|---|---|
| **EF Core solo para el modelo propio transaccional; Dapper para todo lo remoto/federado** | `StationManager` (506 líneas) y `AuditManager` (137) no reciben `AppDbContext`: construyen `SqlConnection` directamente. `TransaccionesProcesadasManager` es **híbrido**: EF para `TransaccionesProcesadas`, `SqlConnection` manual para `EstacionesMaestras`. |
| **Interfaz `IXxxManager` + implementación `XxxManager`, registrada Scoped** | Patrón consistente en 5 módulos. Excepción: `ServiceManager` se registra como **tipo concreto** sin interfaz (`Program.cs:53`) porque usa *primary constructor*. |
| **El `DbContext` actúa como Unit of Work implícito** | No hay repositorios, no hay patrón Unit of Work explícito, no hay `IDbContextFactory`. `SaveChanges()` se llama manualmente (a veces por registro dentro de un bucle, ver `UserMigrationService.cs:28`). |
| **Conexión por operación, no connection pool por manager** | Propiedades que **crean una `SqlConnection` nueva en cada acceso**: `StationManager.cs:31` (`StationsConnection`) y `:33` (`LocalConnection`). Correcto porque Dapper abre/cierra automáticamente bajo `using`. |
| **Estado de progreso en memoria (`static Dictionary`)** | `TransaccionesProcesadasJob._progreso` (`:13`) y `StationManager.ProgressTracker` (`:20`). `Dictionary` no es thread-safe y hay escrituras desde múltiples hilos. El estado se pierde al reiniciar el proceso. |
| **Fire-and-forget para operaciones largas** | `ExecuteBulkSearch` lanza `Task.Run` sin `await` (`:423-428`) para las N tareas por estación; encadena un `Task.Delay(5 min)` para purgar el tracker. El request retorna `searchId` de inmediato. |
| **Concurrencia limitada con semáforo** | `SemaphoreSlim(15)` en `ExecuteBulkSearch:300` + `Interlocked.Increment` para el contador (`:415`). |
| **Particionado por límite de parámetros SQL Server** | Lotes de **1 000 folios** por query (`:343`) para no exceder el máximo de 2 100 parámetros. |
| **Transacciones solo donde son necesarias** | Única transacción explícita: `StationManager.SaveHistory` (`BeginTransaction`/`Commit`/`Rollback`, `:232-252`). El resto de escrituras múltiples se ejecuta sin atomicidad global. |
| **Background jobs solo para un caso** | Hangfire se usa únicamente para la inserción masiva de `TransaccionesProcesadas`. No hay jobs recurrentes (`RecurringJob`), no hay `IRecurringJobManager`. |
| **Generación de documentos server-side** | Rotativa (wkhtmltopdf.exe) para PDF, ClosedXML para Excel, `CsvHelper` propio para CSV. Los tres tienen el binario/lógica en el servidor. |
| **Captura de evidencia en el cliente** | `html2canvas` captura el DOM y lo manda como base64 al endpoint `SaveEvidence`, que escribe un PNG en disco local del servidor. |
| **Sin capa de servicios externa** | Toda la lógica de negocio vive en los Managers, incluidos los concerns de infraestructura (generar Excel, generar PDF, escribir CSV, escribir PNG). |
| **Razor con JS inline en vistas** | No hay bundle de scripts de aplicación. La lógica de cliente (SheetJS, polling, html2canvas, escapeHtml) vive en bloques `<script>` dentro de los `.cshtml`. `wwwroot/js/site.js` tiene 4 líneas sin contenido real. |

### 3.3 Grafo de inyección de dependencias

| Contrato | Implementación | Lifetime | Dependencias del constructor |
|---|---|---|---|
| `AppDbContext` | `AppDbContext` | **Scoped** | `DbContextOptions<AppDbContext>` |
| `ServiceManager` *(tipo concreto, sin interfaz)* | `ServiceManager` | **Scoped** | `AppDbContext` *(primary constructor)* |
| `IStationManager` | `StationManager` | **Scoped** | `IConfiguration`, `ILogger<StationManager>` |
| `ITransactionManager` | `TransactionManager` | **Scoped** | `AppDbContext`, `ILogger<TransactionManager>` |
| `IUserManager` | `UserManager` | **Scoped** | `AppDbContext`, `ILogger<UserManager>`, `IPasswordHasher<User>` |
| `IPasswordHasher<User>` | `PasswordHasher<User>` | **Scoped** | — *(framework)* |
| `IAuditManager` | `AuditManager` | **Scoped** | `IConfiguration`, `ILogger<AuditManager>` |
| `ITransaccionesProcesadasManager` | `TransaccionesProcesadasManager` | **Scoped** | `AppDbContext`, `IConfiguration`, `ILogger<…>`; crea `new ExcelTransaccionesReader()` en el ctor (`:28`) |
| `TransaccionesProcesadasJob` | `TransaccionesProcesadasJob` | **Singleton** | `IServiceScopeFactory` |
| `PasswordService` | `PasswordService` | **Scoped** | — *(crea su propio `PasswordHasher<object>`)* |
| `BackgroundProcessingServer` | Hangfire HostedService | **Singleton** | — *(registrado por `AddHangfireServer()`)* |

**No registrados en DI (instanciación manual):**
- `HangfireAuthorizationFilter` → `new` en `Program.cs:102`
- `ExcelTransaccionesReader` → `new` en `TransaccionesProcesadasManager.cs:28`
- `UserMigrationService` → registro comentado en `Program.cs:77`
- `LogAnalyzerManager` / `ILogAnalyzerManager` → clases vacías, nunca registradas

---

## 4. Stack tecnológico

### 4.1 Paquetes NuGet (`MoneyFlow.csproj`)

| Paquete | Versión | Uso real en el código |
|---|---|---|
| ClosedXML | 0.105.0 | Lectura Excel (`ExcelTransaccionesReader.cs:2`), escritura Excel (`StationManager.cs:1`) |
| Dapper | 2.1.66 | SQL directo a `oxxogas`, linked servers y BD local |
| **EPPlus** | 8.4.2 | **Declarado, sin uso.** No hay ningún `using OfficeOpenXml`. Duplica funcionalidad de ClosedXML y exige licencia comercial |
| Hangfire | 1.8.23 | Núcleo: `AddHangfire`, `BackgroundJob.Enqueue`, `CompatibilityLevel`, `[AutomaticRetry]` |
| Hangfire.AspNetCore | 1.8.23 | `UseHangfireDashboard`, `AddHangfireServer`, `DashboardOptions` |
| Hangfire.SqlServer | 1.8.23 | `UseSqlServerStorage` |
| **Microsoft.AspNetCore.Authentication.JwtBearer** | 9.0.14 | **Declarado, sin uso.** No hay `AddJwtBearer` ni tipos JWT. La app es 100 % cookie |
| Microsoft.AspNetCore.Identity | 2.3.9 | Solo `IPasswordHasher<T>` / `PasswordHasher<T>` (paquete legacy 2.x en proyecto net9.0) |
| **Microsoft.Data.Sqlite** | 10.0.5 | **Declarado, sin uso** |
| Microsoft.EntityFrameworkCore.SqlServer | 9.0.7 | `UseSqlServer` |
| Microsoft.EntityFrameworkCore.Tools | 9.0.7 | CLI de migraciones (design-time) |
| Rotativa.AspNetCore | 1.4.0 | `ViewAsPdf` en `AuditController.cs:57` |
| Serilog.AspNetCore | 10.0.0 | `UseSerilog()` |
| Serilog.Sinks.File | 7.0.0 | Rolling diario a `C:\LogsAppTransaction\log-.txt` |
| **System.IO.Pipelines** | 10.0.5 | **Declarado, sin uso** + v10 en target net9.0 |
| **System.Text.Json** | 10.0.5 | **Declarado, sin uso directo** + v10 en target net9.0 |

**Proyecto de tests** (`MoneyFlow.Tests/MoneyFlow.Tests.csproj`): `Microsoft.NET.Test.Sdk` 17.11.1, `xunit` 2.9.2, `xunit.runner.visualstudio` 2.8.2.

### 4.2 Propiedades del proyecto
- `TargetFramework`: `net9.0`
- `Nullable`: `enable` (genera 115 warnings de nulabilidad; 0 errores de compilación)
- `ImplicitUsings`: `enable`
- `Microsoft.NET.Sdk.Web`

### 4.3 Librerías front-end

| Librería | Versión | Origen | Dónde se usa |
|---|---|---|---|
| Bootstrap | 5.x | Local `wwwroot/lib/bootstrap/dist/` | Layout, navbar, modales, tablas |
| jQuery | 3.x | Local `wwwroot/lib/jquery/dist/` | Dependencia de todo lo anterior |
| jQuery Validation (+Unobtrusive) | — | Local `wwwroot/lib/` | `_ValidationScriptsPartial.cshtml` |
| **DataTables** | **1.13.6** | CDN `cdn.datatables.net` | `Audit/Index`, `Audit/AuditDetail`, `Station/Index`, `Station/ViewReceipts`, `Transaction/History` |
| **Select2** | **4.1.0-rc.0** | CDN `cdn.jsdelivr.net` | `Transaction/Index` (`#cboService`) |
| **SweetAlert2** | **11.26.21** | CDN `cdn.jsdelivr.net` | 6 vistas (alertas, confirmaciones, inputs, loading) |
| **Bootstrap Icons** | **1.11.3** | CDN `cdn.jsdelivr.net` | Layout y botones |
| **Chart.js** | *(sin versión fijada)* | CDN `cdn.jsdelivr.net/npm/chart.js` | `Audit/AuditDetail` (gráfica de diferencias) |
| **html2canvas** | **1.4.1** | CDN `cdnjs.cloudflare.com` | `Station/ViewReceipts` (evidencia PNG) |
| **SheetJS (XLSX)** | **0.18.5** | CDN `cdnjs.cloudflare.com` | `Station/Index`, `Transaction/History` |

---

## 5. Configuración

### 5.1 `appsettings.json` (NO versionado — `.gitignore:364`)

| Clave | Valor (enmascarado) | Consumidor |
|---|---|---|
| `ConnectionStrings:LocalDb` | `Server=C0DZJS3\SQLEXPRESS;Database=MoneyFlowDb;Trusted_Connection=True;TrustServerCertificate=True` | EF Core (`Program.cs:49`), Hangfire (`:65,71`), `StationManager`, `TransaccionesProcesadasManager` |
| `ConnectionStrings:StationsDb` | `Server=10.52.21.11;Database=oxxogas;User Id=****;Password=********;TrustServerCertificate=True` | `StationManager.cs:25`, `AuditManager.cs:25` |
| `Logging:LogLevel:Default` | `Information` | **Inerte** — Serilog configurado en código ignora esta sección |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | **Inerte** |
| `jwt:key` / `jwt:issuer` / `jwt:audience` / `jwt:expiresInMinutes` | `MoneyFlowApi` / `MoneyFlowApiUsers` / `60` | **Configuración huérfana.** No hay `AddJwtBearer` ni lectura de estas claves |
| `AllowedHosts` | `*` | Filtro de Host |

> El archivo está en `.gitignore` pero es **obligatorio para arrancar**. Un clon limpio del repositorio no incluye `appsettings.json` y la aplicación falla al iniciar.

### 5.2 Otros archivos de configuración

| Archivo | Estado |
|---|---|
| `appsettings.Development.json` | Solo contiene `Logging` (duplicado e inerte). No define connection strings |
| `Properties/launchSettings.json` | Un solo perfil `http` → `http://localhost:5184`, env `Development` |
| `README.md` | 1 línea: `# MoneyFlow` |
| `appsettings.Production.json`, `.Staging.json` | No existen |
| User Secrets | No configurados (no hay `UserSecretsId`) |
| Variables de entorno referenciadas desde código | Ninguna |

### 5.3 Logging
Serilog está configurado **en código** (`Program.cs:36-44`), no desde `appsettings`:
- `MinimumLevel.Information()`
- Sink consola
- Sink archivo: `C:\LogsAppTransaction\log-.txt`, `RollingInterval.Day`, template `{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}`
- `builder.Host.UseSerilog()` reemplaza todo el pipeline de logging del framework

---

## 6. Pipeline de arranque

```
WebApplication.CreateBuilder(args)                     Program.cs:15
        │
        ├─ AddControllersWithViews()                    :18
        ├─ AddAuthentication(Cookies).AddCookie(...)   :21-32
        │     LoginPath=/Account/Login
        │     AccessDeniedPath=/Account/AccessDenied
        │     HttpOnly=true | SecurePolicy=None | SameSite=Lax
        │     ExpireTimeSpan=60min | SlidingExpiration=true
        ├─ LoggerConfiguration + UseSerilog()           :36-44
        ├─ AddDbContext<AppDbContext>(UseSqlServer)     :47-50
        ├─ AddScoped<…> (6 managers + hasher)           :53-62
        ├─ AddHangfire(...) + AddHangfireServer()       :66-73
        ├─ AddSingleton<TransaccionesProcesadasJob>()   :74
        ├─ AddScoped<PasswordService>()                  :78
        └─ RotativaConfiguration.Setup(WebRoot,"Rotativa") :82
        │
   app.Build()                                         :84
        │
        ▼  ══════ PETICIÓN HTTP ══════
        │
   ┌────┴─────────────────────────────────────────────────────────┐
   │ 1. if (!IsDevelopment) UseExceptionHandler("/Home/Error")     │ :87-90
   │    ⚠ No hay UseDeveloperExceptionPage() en Development      │
   ├──────────────────────────────────────────────────────────────┤
   │ 2. UseRouting()                                              │ :91
   ├──────────────────────────────────────────────────────────────┤
   │ 3. MapStaticAssets()   [ENDPOINT .NET 9, no middleware]       │ :93
   │    Sirve wwwroot (incluye Rotativa/*.exe) con fingerprint    │
   ├──────────────────────────────────────────────────────────────┤
   │ 4. UseAuthentication()      ← lee HttpContext.User           │ :96
   │    ★ Debe ir DESPUÉS de UseRouting                          │
   ├──────────────────────────────────────────────────────────────┤
   │ 5. UseAuthorization()       ← evalúa [Authorize]             │ :97
   │    ★ Debe ir DESPUÉS de UseAuthentication                   │
   │    → 302 a /Account/Login si no hay cookie válida           │
   │    → 302 a /Account/AccessDenied en 403 (ruta inexistente)  │
   ├──────────────────────────────────────────────────────────────┤
   │ 6. UseHangfireDashboard("/hangfire")                         │ :100-103
   │    Authorization = [new HangfireAuthorizationFilter()]       │
   │    ★ Depende de que UseAuthentication ya haya poblado User  │
   ├──────────────────────────────────────────────────────────────┤
   │ 7. MapControllerRoute("default",                             │ :105-108
   │        "{controller=Account}/{action=Login}/{id?}")           │
   │    .WithStaticAssets()                                      │
   │    ★ ÚNICA ruta. Default = Account/Login                    │
   └──────────────────────────────────────────────────────────────┘
        │
        ▼
   Controllers (MVC dispatch)
        │
        ▼
   app.Run()                                              :117
        │
        └─ HostedServices: BackgroundProcessingServer (Hangfire, 20 workers por defecto)
```

**Nota:** El bloque que ejecutaba `UserMigrationService` al arranque está comentado (`Program.cs:110-115`). No hay llamadas a `Database.Migrate()` ni `EnsureCreated()` en ningún punto.

---

## 7. Autenticación y autorización

### 7.1 Mecanismo
Autenticación por **cookie** (`CookieAuthenticationDefaults.AuthenticationScheme` = `"Cookies"`). **No hay JWT** en el flujo real, pese al paquete `JwtBearer` referenciado y las claves `jwt:*` en configuración.

### 7.2 Construcción de la identidad (`AccountController.cs:34-92`)

Al login exitoso se crea:
```csharp
var claims = new List<Claim>
{
    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),  // → int.Parse en controllers
    new Claim(ClaimTypes.Name,       user.Name),                  // FullName
    new Claim(ClaimTypes.Email,      user.Email)
};
var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
var principal = new ClaimsPrincipal(identity);
await HttpContext.SignInAsync(
    CookieAuthenticationDefaults.AuthenticationScheme,
    principal,
    new AuthenticationProperties
    {
        IsPersistent      = true,
        AllowRefresh     = true,
        ExpiresUtc       = DateTime.UtcNow.AddHours(1)
    });
return RedirectToAction("Index", "Home");
```

**Claims consumidos en el código:**
- `ClaimTypes.NameIdentifier` → `UserId` (usado por `ServiceController.Index:18`, `TransactionController.GetServicesByType:30`, `GetHistoryTransactions:67`)
- `ClaimTypes.Name` → `User.Identity.Name` (usado como `AppliedBy` en `StationController.ProcessFinalUpdate:97`, y como `user` en `ProcessBulk:169`)
- `ClaimTypes.Email` → almacenado pero no consumido

### 7.3 Atributos de autorización

| Controller | Atributo | Excepciones |
|---|---|---|
| `AccountController` | `[AllowAnonymous]` (`:11`) | — |
| `HomeController` | `[Authorize]` (`:11`) | — |
| `SearchController` | `[Authorize]` (`:6`) | — |
| `ServiceController` | `[Authorize]` (`:10`) | `Delete` además `[ValidateAntiForgeryToken]` (`:97`) |
| `StationController` | `[Authorize]` (`:11`) | — |
| `TransactionController` | `[Authorize]` (`:10`) | — |
| `AuditController` | `[Authorize]` (`:11`) | — |
| `TransaccionesProcesadasController` | `[Authorize]` (`:8`) | — |

**No existe sistema de roles.** No hay `AddIdentity`, `IdentityOptions`, ni políticas de autorización. El único claim de autorización es el `IsAuthenticated` implícito del esquema cookie.

**Anti-forgery:** `@Html.AntiForgeryToken()` está en el `<head>` del layout (`_Layout.cshtml:7`) pero **solo un action tiene `[ValidateAntiForgeryToken]` explícito** (`ServiceController.Delete:97`). Los demás POST — incluidos los que mutan datos (`ProcessFinalUpdate`, `SaveEvidence`, `ProcessBulk`, `Guardar`, `EliminarResultados`, `Truncar`) — no lo llevan.

### 7.4 Dashboard de Hangfire
Ruta `/hangfire` (`Program.cs:100-103`). Filtro de autorización:
```csharp
public bool Authorize(DashboardContext context)
    => context.GetHttpContext()?.User?.Identity?.IsAuthenticated == true;
```
(`Utilities/HangfireAuthorizationFilter.cs:11-15`) — verifica únicamente que exista identidad autenticada, sin comprobar rol.

### 7.5 Rutas referenciadas que no existen
| Referencia | Ubicación | Estado |
|---|---|---|
| `/Account/AccessDenied` | `Program.cs:24` (`AccessDeniedPath`) | `AccountController` solo expone `Login`. Un 403 redirige a un 404 |
| `/Receipts/Index` | `_Layout.cshtml:61` | No existe `ReceiptsController` |
| `href="#"` | `_Layout.cshtml:51` y `:65` | Enlaces de relleno del template |

---

## 8. Front-end

### 8.1 Layout maestro (`Views/Shared/_Layout.cshtml`, 98 líneas)

Estructura: `<head>` con librerías → `<nav>` Bootstrap → `<main>` con `@RenderBody()` → `<footer>` → bloque de `<script>` → `@await RenderSectionAsync("Scripts", false)`.

```html
<head>  (:1-18)
  Bootstrap 5.3 (local) · site.css · MoneyFlow.styles.css (CSS isolation)
  DataTables 1.13.6 CSS · Select2 4.1.0-rc.0 CSS · SweetAlert2 11.26.21 CSS
  Bootstrap Icons 1.11.3 CSS · @Html.AntiForgeryToken()  (:7)
</head>
<nav>   (:20-78)
  Brand → Home/Index
  · Home
  · dropdown "Transactions": Transaction/Index · Transaction/HistoryTransactions · [href="#"]
  · dropdown "Process":       Receipts/Index [roto] · Station/Index ·
                              TransaccionesProcesadas/Index · [href="#"]
  · Logout → Home/Logout   (solo si User.Identity.IsAuthenticated, :68-73)
</nav>
<main>  (:79-83)  @RenderBody()
<footer>(:85-89)  "© 2026 - MoneyFlow - Privacy" → Home/Privacy
<script> (:90-97)
  jquery.min.js (local) · bootstrap.bundle.min.js (local) · site.js (local, vacío)
  DataTables 1.13.6 · Select2 4.1.0-rc.0 · SweetAlert2 11.26.21
  @await RenderSectionAsync("Scripts", false)
</script>
```

**Configuración global:** `Views/_ViewStart.cshtml` fija `Layout = "_Layout"`. `Views/_ViewImports.cshtml` importa `MoneyFlow`, `MoneyFlow.Models`, `System.Security.Claims` y los TagHelpers de MVC.

**Vistas que anulan el layout:**
- `Account/Login.cshtml:3` → `Layout = null`
- `Audit/PdfReport.cshtml` → vista para Rotativa, HTML autónomo con estilos inline

### 8.2 Inventario de vistas

| Vista | Líneas | Modelo | Comportamiento |
|---|---|---|---|
| `Account/Login.cshtml` | 62 | `LoginViewModel` | Formulario Email/Password, `autocomplete="off"`, checkbox "Remember Me", `Forgot Password` comentado. Errores vía `TempData["ErrorMessage"]` → `Swal.fire` |
| `Home/Index.cshtml` | 11 | — | Saludo con `ClaimTypes.Name` |
| `Home/Privacy.cshtml` | 6 | — | Estática |
| `Service/Index.cshtml` | 105 | `List<ServiceViewModel>` | Tabla con Edit (link) y Delete (modal Bootstrap + form POST con antiforgery) |
| `Service/Create.cshtml` | 36 | `ServiceViewModel` | Formulario: Name (text), Type (select income/expense) |
| `Service/Detail.cshtml` | 39 | `ServiceViewModel` | Formulario de edición con `ServiceId` hidden |
| `Audit/Index.cshtml` | 78 | `List<AuditDTO>` | DataTables `#auditTable`, pageLength 10, orden `[[0,"desc"]]`, idioma ES. Enlace "Ver Detalle" |
| `Audit/AuditDetail.cshtml` | 142 | `AuditDetailViewModel` | Genera JSON en Razor (`labelsJson`, `dataJson`) para Chart.js. Dos DataTables (`#inventoryTable`, `#detailTable`). Badges Correcto/Sobrante/Faltante por diferencia |
| `Audit/PdfReport.cshtml` | 157 | `AuditDetailViewModel` | HTML autónomo para wkhtmltopdf. Tabla multi-header con rowspan/colspan (Rack 1-4 + General) |
| `Station/Index.cshtml` | 536 | `List<StationViewModel>` | **Vista más compleja.** Búsqueda + carga Excel/CSV + tabla masiva + progreso + export |
| `Station/ViewReceipts.cshtml` | 339 | `IEnumerable<ReceiptViewModel>` | Recepción + corrección masiva de remisiones + evidencias |
| `TransaccionesProcesadas/Index.cshtml` | 313 | — (JSON) | Carga Excel + preview + job + progreso |
| `Transaction/Index.cshtml` | 158 | — (form) | Select2 + fetch JSON + validación Swal |
| `Transaction/History.cshtml` | 204 | — (AJAX) | DataTable AJAX con rango de fechas |
| `Shared/Error.cshtml` | 25 | `ErrorViewModel` | Muestra `RequestId` |
| `Shared/_ValidationScriptsPartial.cshtml` | 2 | — | jQuery Validation + Unobtrusive |

### 8.3 Recursos estáticos (`wwwroot/`)

| Ruta | Contenido |
|---|---|
| `lib/bootstrap/dist/` | Bootstrap 5.3 completo (css + js + maps) |
| `lib/jquery/dist/` | jQuery 3.x (completo, slim, min, maps) |
| `lib/jquery-validation/` + `lib/jquery-validation-unobtrusive/` | Plugins de validación |
| `css/site.css` | 31 líneas. Estilos globales, `body { margin-bottom: 60px }` por footer absoluto |
| `css/login.css` | 85 líneas. Tarjeta de login centrada, icono púrpura `#8e44ad` |
| `js/site.js` | 4 líneas. Placeholder sin lógica |
| `Rotativa/wkhtmltopdf.exe` | 28,8 MB (binario versionado) |
| `Rotativa/wkhtmltoimage.exe` | 28,8 MB (binario versionado) |

> `MoneyFlow.styles.css` se referencia en el layout (`:11`) pero no existe en `wwwroot/`; lo produce el pipeline de CSS scoped de Razor.

---

## 9. Modelo de datos

### 9.1 Diagrama de entidades EF Core

```
              ┌───────────────────────────────────────────┐
              │  dbo.[User]                               │
              │  UserId       int IDENTITY  ← PK          │
              │  FullName     nvarchar(max) NOT NULL       │
              │  Email        nvarchar(max) NOT NULL       │  ← sin índice único
              │  Password     nvarchar(max) NULL ★         │
              │  PasswordHash nvarchar(max) NOT NULL  ★    │
              └───────┬───────────────────────┬───────────┘
        FK Restrict  │                       │  FK Restrict
      1 ─────────────┘                       └───────────── 1
                   │ N                                       │ N
      ┌────────────▼──────────────┐            ┌────────────▼──────────────┐
      │ dbo.[Service]             │            │ dbo.[Transaction]         │
      │ ServiceId  int IDENTITY PK │            │ TransactionId int ID PK   │
      │ UserId     int FK          │◄─── FK ────│ ServiceId    int FK       │
      │ Name       nvarchar(max)   │  Restrict  │ UserId       int FK       │
      │ Type       nvarchar(max)   │            │ Comment     nvarchar(max) │
      │ IX: UserId (automático)   │            │ Date        date          │
      └───────────────────────────┘            │ TotalAmount decimal(10,2) │
                                              │ IX: ServiceId, UserId      │
                                              └───────────────────────────┘

   ┌─────────────────────────────────────────────────────────────┐
   │ dbo.TransaccionesProcesadas     (Entidad: TransaccionProcesada)
   │   Id          int IDENTITY  ← PK CLUSTERED                    │
   │   IdEstacion  int NOT NULL   (FK lógica a EstacionesMaestras, sin constraint)
   │   CR          nvarchar(max)   (Código de Retail)
   │   LS          nvarchar(max)   (Link Server de la estación)
   │   Nombre      nvarchar(max)
   │   FoliosTotales nvarchar(max) (pseudo-CSV: "F1,F2,F3")
   │   FechaCarga  datetime DEFAULT GETDATE()
   │   SIN índices secundarios
   └─────────────────────────────────────────────────────────────┘

   ★ User.PasswordHash  → existe en la BD real pero NO en migraciones/snapshot
   ★ User.Password      → la entidad lo declara string?, la migración lo crea NOT NULL
```

### 9.2 Estado del esquema: dos vías de verdad

| Vía | Contenido | Creado por |
|---|---|---|
| **Migración EF** (`Migrations/20260121230151_FirstMigration.cs`, 113 líneas) | Tablas `User` (sin `PasswordHash`), `Service`, `Transaction`. Índices `IX_Service_UserId`, `IX_Transaction_ServiceId`, `IX_Transaction_UserId`. `InsertData` de un usuario con `Password` en claro, sin `PasswordHash` | `dotnet ef migrations` |
| **Snapshot** (`AppDbContextModelSnapshot.cs`, 157 líneas) | **Solo** `Service`, `Transaction`, `User`. **No incluye `TransaccionProcesada` ni `User.PasswordHash`** | Generado automáticamente |
| **Script SQL** (`Scripts/transacciones_procesadas.sql`, 81 líneas) | `CREATE TABLE dbo.TransaccionesProcesadas` (si no existe), `ALTER TABLE dbo.[User] ADD PasswordHash`, `UPDATE` del hash estático del usuario seed. **Idempotente** (3 guardas `IF … IS NULL`) | Ejecución manual |

**Consecuencia observable:** `dotnet ef database update` en una base limpia crearía solo `User`, `Service` y `Transaction`. Las tablas `TransaccionesProcesadas` y la columna `User.PasswordHash` — ambas usadas por el código en runtime — dependen del script SQL manual. No hay ninguna llamada a `Database.Migrate()` en el arranque que sincronice ambas vías.

### 9.3 Tablas usadas en runtime sin alta en migraciones ni scripts

Estas 4 tablas se consultan/escriben **solo con Dapper** y **no existen en ningún archivo de esquema del repositorio**:

| Tabla | Columnas usadas | Operaciones |
|---|---|---|
| `EstacionesMaestras` | `IdEstacion`, `LS`, `cr`, `nombre`, `ACtiva` (typo del nombre real) | `SELECT` (mapa LS, metadata), 2 lecturas |
| `CorrectionHistory` | `Instance`, `POId`, `OldRemission`, `NewRemission`, `AppliedAt`, `AppliedBy` | `SELECT DISTINCT POId`, `INSERT` en bucle con transacción |
| `LocalBulkSearchResults` | `Folio`, `OrderId`, `Tipo`, `Total`, `NombreEmpleado`, `EstadoEmpleado`, `EmpleadoEstacion`, `RoleName`, `CR`, `Estacion`, `Created`, `SearchId`, `UserExecution` | `INSERT` multi-fila, `SELECT` para Excel, `COUNT(*)`, `TRUNCATE` |
| `BulkSearchErrors` | `SearchId`, `LS`, `NombreEstacion`, `ErrorMsg` | `INSERT`, `SELECT`, `TRUNCATE` |

### 9.4 Convenciones del modelo

- No hay ningún `enum` en el proyecto. Campos que serían enumeraciones (`Service.Type`, `ReceiptViewModel.InventoryAssignationType`, `AuditDTO.AuditType`, `BulkSearchResultDTO.EstadoEmpleado`) son `string`/`nvarchar` sin validación.
- No hay campos de auditoría (`CreatedDate`, `UpdatedDate`, `CreatedBy`, `RowVersion`, `IsDeleted`) en ninguna entidad. La única columna temporal del modelo propio es `TransaccionProcesada.FechaCarga`.
- No hay índices declarados explícitamente en `OnModelCreating` (solo los 3 automáticos de FK).
- `Entities/Receipt.cs` es una **clase vacía** sin `DbSet` — no genera ninguna tabla. El concepto "Receipt" real vive en `Models/ReceiptViewModel.cs`.
- Los `DbSet` usan nombre singular salvo `TransaccionesProcesadas`.

---

## 10. Fuentes de datos y consultas

### 10.1 Resumen por conexión

| Conexión | Tecnología | Managers consumidores |
|---|---|---|
| `LocalDb` (MoneyFlowDb) | **EF Core** | `UserManager`, `TransactionManager`, `ServiceManager`, `TransaccionesProcesadasJob` |
| `LocalDb` | **Dapper** | `StationManager` (EstacionesMaestras, CorrectionHistory, LocalBulkSearchResults, BulkSearchErrors), `TransaccionesProcesadasManager.ObtenerMapaEstaciones` |
| `StationsDb` (oxxogas) | **Dapper** | `StationManager`, `AuditManager` |
| `[LS].gaxpos.*` (linked servers) | **Dapper** | `StationManager`, `AuditManager` |
| `LocalDb` | **Hangfire SqlServerStorage** | Tablas `HangFire.*` |

### 10.2 Catálogo de consultas

#### `StationManager` — el archivo más grande (506 líneas)

| Método | Línea | Conexión | SQL (resumido) | Tipo |
|---|---|---|---|---|
| `SearchStations` | 38-64 | `oxxogas` | `SELECT ER.IdEstacion, ER.cr AS CR, REPLACE(RE.nombre,' ','') AS LS, ER.nombre AS Nombre, RE.Estacion, RE.ACTIVO AS Activo FROM oxxogas..EstacionesReportes ER INNER JOIN oxxogas..relacionestaciones RE ON ER.IdEstacion = RE.EstacionID WHERE RE.ACTIVO = 1 AND (ER.Nombre LIKE '%' + @search + '%' OR ER.cr LIKE '%' + @search + '%') ORDER BY ER.Nombre DESC` | `QueryAsync<StationViewModel>` |
| `GetReceipts` | 80-130 | `[LS]` + local | `SELECT R.ReceiptId, R.RecordTypeId, RT.Description, R.Quantity, R.ReceiptStatusId, R.StatusId, R.POId, R.Notes, R.IsFuel, R.CreatedBy, R.Created, R.LastModifiedBy, R.LastModified, R.CancellationDate, R.InventoryAssignationType FROM [{ls}].gaxpos.[Purchase].[Receipt] R INNER JOIN [{ls}].gaxpos.[Catalog].[RecordType] RT ON R.RecordTypeId = RT.RecordTypeId [WHERE R.ReceiptId IN @Ids] ORDER BY R.ReceiptId DESC` + `SELECT DISTINCT POId FROM CorrectionHistory WHERE Instance = @ls` | `QueryAsync<ReceiptViewModel>` |
| `GetDetailsFromPO` | 159-169 | `[LS]` | `SELECT POId, NumOC, Subtotal, Tax, Total, [Status], CreatedBy, Created, CarrierName, Clave, FromERP, Remission, RemissionDate, StationId FROM [{ls}].gaxpos.[Purchase].[PO] WHERE POId IN @Ids` | `QueryAsync<POVViewModel>` |
| `UpdateSpecificRemissions` | 188-203 | `[LS]` | `UPDATE [{ls}].gaxpos.[Purchase].[PO] SET Remission = @Remission WHERE POId = @POId` — **una sentencia por elemento del bucle, sin transacción** | `ExecuteAsync` (N) |
| `SaveHistory` | 226-248 | local | `INSERT INTO CorrectionHistory (Instance, POId, OldRemission, NewRemission, AppliedAt, AppliedBy) VALUES (@Instance, @POId, @OldRemission, @NewRemission, @AppliedAt, @AppliedBy)` — bucle dentro de `BeginTransaction()` | `ExecuteAsync` (N) |
| `GetProgress` | 275-278 | local | `SELECT LS FROM BulkSearchErrors WHERE SearchId = @searchId` — **llamada síncrona** (`Query` sin await) | `Query<string>` |
| `ExecuteBulkSearch` | 315-386 | `[LS]` + local | Ver desglose en 10.3 | `QueryAsync<dynamic>` + `ExecuteAsync` |
| `ExportResultsToExcel` | 438-444 | local | `SELECT Folio, OrderId, Tipo, Total, NombreEmpleado, EstadoEmpleado, EmpleadoEstacion, RoleName, CR, Estacion, Created FROM LocalBulkSearchResults ORDER BY Estacion, Created DESC` | `QueryAsync<BulkSearchResultDTO>` |
| `ObtenerConteoResultados` | 470 | local | `SELECT COUNT(*) FROM LocalBulkSearchResults` — **síncrono** | `ExecuteScalar<int>` |
| `TruncarResultados` | 477-478 | local | `TRUNCATE TABLE LocalBulkSearchResults` + `TRUNCATE TABLE BulkSearchErrors` — dos sentencias secuenciales | `Execute` |
| `GetStationMetadata` | 485-499 | local | `SELECT IdEstacion, cr AS CR, REPLACE(LS,' ','') AS LS, nombre AS Nombre, IdEstacion AS Estacion, ACtiva AS Activo FROM EstacionesMaestras WHERE IdEstacion IN @ids ORDER BY Nombre DESC` | `QueryAsync<StationViewModel>` |

#### `AuditManager` — 100 % base remota (`StationsDb`, 137 líneas)

| Método | Línea | SQL (resumido) |
|---|---|---|
| `GetAudits` | 32-38 | `SELECT AuditId, StationId, StatusName, Folio, Comments, MotiveAuditAdjustmentName, AuditType, StartDate, EndDate FROM [{station}].[GAXPOS].[Inventory].[Audit] ORDER BY Created DESC` |
| `GetproductsByAudit` | 48-56 | `SELECT ProductAuditInventoryId, ProductId, ProductName, TheoreticalInventory, ActualInventory, InventoryDifference FROM [{station}].[GAXPOS].[Inventory].[ProductAuditInventory] WHERE AuditId = @auditId` |
| `GetAuditDetails` | 73-89 | `SELECT pd.ProductAuditInventoryDetailId, pd.LocationId, l.LocationName, pd.TheoreticalInventory, pd.ActualInventory, pd.ProductAuditInventoryId FROM [{station}].[GAXPOS].[Inventory].[ProductAuditInventoryDetail] pd INNER JOIN [{station}].[GAXPOS].[Inventory].[Location] l ON pd.LocationId = l.LocationId WHERE pd.ProductAuditInventoryId IN @Ids ORDER BY pd.ProductAuditInventoryId DESC` |
| `GetFullAudit` | 99-135 | No ejecuta SQL: orquesta los dos anteriores y compone `AuditDetailViewModel` |

#### Consultas EF Core

| Ubicación | Operación |
|---|---|
| `UserManager.cs:33-35` | `User.Where(u => u.Email == email && u.Password == password)` — **comparación de contraseña en texto plano en el WHERE** |
| `UserManager.cs:64-66` | `User.Where(u => u.Email == email).FirstOrDefaultAsync()` |
| `UserManager.cs:101` | `User.FirstOrDefaultAsync(u => u.Email == email)` (para `VerifyHashedPassword`) |
| `TransactionManager.cs:30-31` | `Transaction.Add` + `SaveChanges()` |
| `TransactionManager.cs:49-59` | `Transaction.Where(UserId == @u && Date >= @start && Date <= @end)` + proyección a `HistoryTransactionDTO`. **`ToList()` síncrono dentro de un método `async`** |
| `ServiceManager.cs:19-138` | CRUD completo. **Todas las operaciones son síncronas** |
| `TransaccionesProcesadasManager.cs:100` | `TransaccionesProcesadas.CountAsync()` |
| `TransaccionesProcesadasManager.cs:105` | `ExecuteSqlRawAsync("TRUNCATE TABLE [dbo].[TransaccionesProcesadas]")` |
| `TransaccionesProcesadasManager.cs:110-112` | `OrderBy(d => d.Id).ToListAsync()` — **carga la tabla completa en memoria** |
| `TransaccionesProcesadasJob.cs:49-51` | `AddRange` + `SaveChangesAsync()` + `ChangeTracker.Clear()` por lotes de 500 |

### 10.3 Desglose de la consulta de búsqueda masiva (`ExecuteBulkSearch`, líneas 290-431)

```
INICIO
  searchId = Guid.NewGuid()                                    :292
  filtrar request donde FoliosCsv no vacío                     :293
  totalStaciones = request.Count                               :294
  ProgressTracker[searchId] = (0, total)                       :298
  semaphore = new SemaphoreSlim(15)                            :300

  Para CADA estación (tasks en paralelo, limitadas a 15):
    ├─ await semaphore.WaitAsync()                             :303
    ├─ folios = item.FoliosCsv.Split(',').Trim().Distinct()    :307-311
    ├─ Construir query dinámica:                               :315-338
    │    DECLARE @CR_Remote VARCHAR(20), @Estacion_Remote VARCHAR(100);
    │    SELECT TOP 1 @CR_Remote = StationCROracle,
    │                  @Estacion_Remote = @NombreEstacion
    │    FROM [{item.LS}].[GAXPOS].[System].[Stations];
    │
    │    SELECT O.Folio, O.OrderId, O.[Type] AS Tipo, O.Total,
    │           CASE WHEN NULLIF(LTRIM(RTRIM(CONCAT(U.[Name],' ',
    │               U.LastName))),'') IS NULL THEN 'Sin Nombre'
    │                ELSE LTRIM(RTRIM(CONCAT(U.[Name],' ',U.LastName))) END AS NombreEmpleado,
    │           CASE WHEN U.Disabled = 1 THEN 'Deshabilitado'
    │                ELSE 'Habilitado' END AS EstadoEmpleado,
    │           O.EmployeeNumber AS EmpleadoEstacion,
    │           ISNULL(R.Name,'Sin Rol') AS RoleName,
    │           @CR_Remote AS CR, @Estacion_Remote AS Estacion, O.Created
    │    FROM [{item.LS}].[gaxpos].[Sale].[Order] O
    │    LEFT JOIN [{item.LS}].[GAXPOS].Security.Users U ON U.EmployeeNumber = O.EmployeeNumber
    │    LEFT JOIN [{item.LS}].[GAXPOS].Security.Roles R ON R.Id = U.RoleId
    │    WHERE O.Folio IN @Folios
    │    ★ item.LS se INTERPOLA como identificador de base de datos
    │    ★ item.Nombre se PASA como parámetro @NombreEstacion (correcto)
    │
    ├─ Por cada LOTE de 1 000 folios (límite de parámetros SQL):  :343-361
    │    using connStations = StationsConnection
    │    resultadosLote = await QueryAsync(remoteQuery,
    │        new { Folios = lote, NombreEstacion = item.Nombre },
    │        commandTimeout: 60)
    │    ★ Una conexión remota NUEVA por cada lote
    │    resultados.AddRange(resultadosLote)      → List<dynamic> en memoria
    │
    ├─ Si hay resultados:                                       :363-386
    │    using connLocal = LocalConnection
    │    INSERT INTO LocalBulkSearchResults (Folio, OrderId, Tipo, Total,
    │        NombreEmpleado, EstadoEmpleado, EmpleadoEstacion, RoleName,
    │        CR, Estacion, Created, SearchId, UserExecution) VALUES (...)
    │    ★ Multi-insert de Dapper (un lote por estación), sin transacción
    │
    ├─ catch: registra error                                    :393-411
    │    INSERT INTO BulkSearchErrors(SearchId, LS, NombreEstacion, ErrorMsg)
    │    ★ El error NO se propaga (fallo silencioso por estación)
    │
    └─ finally:                                                 :412-418
         Interlocked.Increment(ref processedCount)   ← atómico
         ProgressTracker[searchId] = (processedCount, total)  ← Dictionary, sin lock
         semaphore.Release()

  Task.Run(async () => {                       :423-428  ← fire-and-forget
      await Task.WhenAll(tasks);
      await Task.Delay(TimeSpan.FromMinutes(5));  ← purga diferida
      ProgressTracker.Remove(searchId);
  })

  return searchId                             :430
```

### 10.4 Tratamiento del identificador de instancia de BD (dato de diseño)

El identificador de base de datos (`ls`, `station`, `item.LS`) **no puede parametrizarse** en T-SQL, por lo que se concatena con interpolación de cadena en `[{ls}].[…]`. El proyecto aplica validación en un solo caso:

```csharp
// StationManager.cs:76-77 — ÚNICO punto con validación de identificador
if (string.IsNullOrEmpty(ls) || ls.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
    throw new Exception("Nombre de instancia inválida");
```

Los siguientes métodos **no aplican esta validación**: `GetDetailsFromPO` (`:154`), `UpdateSpecificRemissions` (`:177`), `ExecuteBulkSearch` (`:290`), y los tres métodos de `AuditManager` (`:28`, `:43`, `:67`).

---

## 11. Catálogo de endpoints

Ruta base: `{controller=Account}/{action=Login}/{id?}` (única ruta, `Program.cs:105-107`).

### `AccountController` — `[AllowAnonymous]`

| Acción | HTTP | Línea | Firma | Retorno |
|---|---|---|---|---|
| `Login` | GET | 24-30 | `IActionResult Login()` | `View(new LoginViewModel())` |
| `Login` | POST | 33-92 | `Task<IActionResult> Login(LoginViewModel model)` | `View(model)` con `TempData["ErrorMessage"]` \| `RedirectToAction("Index","Home")` |

### `HomeController` — `[Authorize]`

| Acción | HTTP | Línea | Retorno |
|---|---|---|---|
| `Index` | GET | 21-27 | `View()` |
| `Privacy` | GET | 29-32 | `View()` |
| `Logout` | GET | 35-42 | `SignOutAsync` + `RedirectToAction("Login","Account")` |
| `Error` | GET | 45-49 | `View(new ErrorViewModel { RequestId = … })` |

### `SearchController` — `[Authorize]`
| `Index` | GET | 9-12 | `View()` — sin lógica, sin manager inyectado |

### `ServiceController` — `[Authorize]`, recibe `ServiceManager` (tipo concreto)

| Acción | HTTP | Línea | Firma | Retorno |
|---|---|---|---|---|
| `Index` | GET | 14-21 | `IActionResult Index()` | `View(List<ServiceViewModel>)` |
| `Create` | GET | 24-28 | `IActionResult Create()` | `View()` |
| `CreateNewService` | POST | 31-56 | `IActionResult CreateNewService(ServiceViewModel sm)` | `View("Create", sm)` \| `RedirectToAction("Index")` + `TempData` |
| `Detail` | GET | 59-68 | `IActionResult Detail(int id)` | `View("Detail", sv)` \| `NotFound()` |
| `Updateservice` | POST | 71-94 | `IActionResult Updateservice(ServiceViewModel sm)` | `View("Detail", sm)` \| `RedirectToAction("Index")` + `TempData` |
| `Delete` | POST | 96-114 | `[ValidateAntiForgeryToken] IActionResult Delete(int id)` | `RedirectToAction("Index")` + `TempData/ViewBag` |

### `StationController` — `[Authorize]`, recibe `IStationManager`

| Acción | HTTP | Línea | Firma | Retorno |
|---|---|---|---|---|
| `Index` | GET | 21-24 | `IActionResult Index()` | `View(new List<StationViewModel>())` |
| `Index` | POST | 25-39 | `Task<IActionResult> Index(string search)` | `View(stations)` \| `View(empty)` + `ViewBag.Message` |
| `GetReceipts` | GET | 42-60 | `Task<IActionResult> GetReceipts(string ls, string receiptIds = null)` | `View("ViewReceipts", receipts)` + `ViewBag.LS` \| `RedirectToAction("ViewReceipts")` + `TempData` |
| `GetPODetails` | POST | 63-76 | `Task<IActionResult> GetPODetails([FromBody] BulkRemissionUpdateDTO request)` | `Json(poDetails)` \| `BadRequest({message})` |
| `ProcessFinalUpdate` | POST | 79-110 | `Task<IActionResult> ProcessFinalUpdate([FromBody] BulkRemissionUpdateDTO data)` | `Ok({success,message})` \| `BadRequest({success,message})` |
| `SaveEvidence` | POST | 113-151 | `IActionResult SaveEvidence([FromBody] EvidenceRequest request)` | `Ok()` \| `BadRequest("No image data")` \| `BadRequest("Error al guardar en C:: …")` |
| `GetProgress` | GET | 155-162 | `IActionResult GetProgress(Guid searchId)` | `Ok(progressInfo)` |
| `ProcessBulk` | POST | 163-173 | `Task<IActionResult> ProcessBulk([FromBody] List<StationFolioDTO> request)` | `Ok({searchId})` \| `BadRequest` |
| `DownloadExcel` | GET | 175-189 | `Task<IActionResult> DownloadExcel()` | `File(byte[], "…spreadsheetml.sheet", "Reporte_Masivo_{yyyyMMdd_HHmm}.xlsx")` \| `BadRequest` |
| `ObtenerConteoResultados` | GET | 191-203 | `IActionResult ObtenerConteoResultados()` | `Ok({total})` \| `BadRequest` |
| `EliminarResultados` | POST | 205-217 | `IActionResult EliminarResultados()` | `Ok({success,message})` \| `BadRequest` |
| `GetStationsMetadata` | POST | 219-225 | `Task<IActionResult> GetStationsMetadata([FromBody] List<int> ids)` | `Ok(metadata)` |

### `TransactionController` — `[Authorize]`, recibe `ServiceManager` + `ITransactionManager`

| Acción | HTTP | Línea | Firma | Retorno |
|---|---|---|---|---|
| `Index` | GET | 20-23 | `IActionResult Index()` | `View()` |
| `GetServicesByType` | GET | 26-33 | `IActionResult GetServicesByType(string type)` | `Ok(services)` |
| `SaveNewTransaction` | POST | 36-54 | `IActionResult SaveNewTransaction([FromBody] TransactionDTO dto)` | `Ok({success,message})` \| `BadRequest({success,message,errors})` |
| `HistoryTransactions` | GET | 57-60 | `IActionResult HistoryTransactions()` | `View("History")` |
| `GetHistoryTransactions` | GET | 63-79 | `Task<IActionResult> GetHistoryTransactions(DateOnly? startDate, DateOnly? endDate)` | `Ok({data})` — fechas nulas → rango del mes actual |

### `AuditController` — `[Authorize]`, recibe `IAuditManager`

| Acción | HTTP | Línea | Firma | Retorno |
|---|---|---|---|---|
| `GetAudits` | GET | 22-30 | `Task<IActionResult> GetAudits(string ls)` | `View("Index", audits)` + `ViewBag.Station` |
| `AuditDetail` | GET | 32-52 | `Task<IActionResult> AuditDetail(string ls, int auditId)` | `View("AuditDetail", model)` |
| `ExportPdf` | GET | 54-65 | `Task<IActionResult> ExportPdf(int auditId, string station)` | `ViewAsPdf("PdfReport", model)` — A4 Landscape, márgenes 5 |

### `TransaccionesProcesadasController` — `[Authorize]`, recibe `ITransaccionesProcesadasManager`

| Acción | HTTP | Línea | Firma | Retorno |
|---|---|---|---|---|
| `Index` | GET | 19-22 | `IActionResult Index()` | `View()` |
| `CargarExcel` | POST | 26-42 | `Task<IActionResult> CargarExcel(IFormFile archivo)` | `Json(ExcelCargaResultDTO)` (con `Errores` si falla, **HTTP 200**) |
| `Guardar` | POST | 46-68 | `IActionResult Guardar([FromBody] List<TransaccionProcesadaDTO> items)` | `Ok({success, jobId})` \| `BadRequest` |
| `GetProgreso` | GET | 71-75 | `IActionResult GetProgreso(Guid jobId)` | `Ok(object)` |
| `ObtenerConteo` | GET | 78-90 | `Task<IActionResult> ObtenerConteo()` | `Ok({total})` \| `BadRequest` |
| `Eliminar` | POST | 93-106 | `Task<IActionResult> Eliminar()` | `Ok({success,message})` \| `BadRequest` |
| `DescargarCsv` | GET | 109-122 | `Task<IActionResult> DescargarCsv()` | `File(byte[], "text/csv; charset=utf-8", "TransaccionesProcesadas_{yyyyMMdd_HHmmss}.csv")` |

### Endpoints no-REST fuera de controllers

| Ruta | Tipo | Ubicación | Acceso |
|---|---|---|---|
| `/hangfire` | Hangfire Dashboard | `Program.cs:100` | `HangfireAuthorizationFilter` (`IsAuthenticated`) |
| `/Account/AccessDenied` | Configurado pero inexistente | `Program.cs:24` | — |

---

## 12. Flujos de negocio

### 12.1 Flujo A — Inicio de sesión

```
[Navegador] GET /Account/Login
     │
     ▼
AccountController.Login()  (GET, :24-30)
     └─ View(new LoginViewModel())  ──►  Account/Login.cshtml  (Layout = null)
     │
[Navegador] POST /Account/Login   { Email, Password, RememberMe }
     │
     ▼
AccountController.Login(LoginViewModel)  (POST, :33-92)
     │
     ├─ 1. if (!ModelState.IsValid) → View(model)                    :36-40
     │
     ├─ 2. GetByEmail(email)                              [EF, Async]  :41
     │       SELECT * FROM User WHERE Email = @email
     │       null → TempData["ErrorMessage"]="Invalid email or password…" → View
     │
     ├─ 3. ValidatePassword(email, password)                [EF, Async] :48
     │       SELECT * FROM User WHERE Email = @email
     │       _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password)
     │       Resultado == Failed → TempData["ErrorMessage"] → View
     │
     ├─ 4. Login(model)                                     [EF, Async] :56
     │       SELECT * FROM User WHERE Email = @email AND Password = @password
     │       ★ Comparación de contraseña EN TEXTO PLANO
     │       null → TempData["ErrorMessage"] → View
     │
     ├─ 5. Construir ClaimsPrincipal                         :64-83
     │       NameIdentifier = UserId, Name = FullName, Email
     │
     ├─ 6. HttpContext.SignInAsync(Cookies, principal, props) :84-88
     │       IsPersistent=true, AllowRefresh=true, ExpiresUtc=UtcNow+1h
     │
     └─ 7. RedirectToAction("Index", "Home")                :90
```

### 12.2 Flujo B — Búsqueda de estaciones + carga masiva de folios

```
FASE 1 — Búsqueda
  GET  /Station/Index                      → View con lista vacía
  POST /Station/Index { search }
        └─ IStationManager.SearchStations(search)
             SELECT … FROM oxxogas..EstacionesReportes ER
             INNER JOIN oxxogas..relacionestaciones RE …
             WHERE RE.ACTIVO=1 AND (Nombre LIKE %@search% OR cr LIKE %@search%)
        └─ View(stations)  →  #stationsTable (DataTables, idioma ES)
           Acciones por fila: "Ver Recibos" | "Agregar a Masivo" | "Auditorías"

FASE 2 — Carga del archivo (todo en el cliente, Station/Index.cshtml)
  Usuario selecciona .xlsx / .xls / .csv  →  #excelInput
     │
     ├─ FileReader + SheetJS 0.18.5:  XLSX.read() → XLSX.utils.sheet_to_json()
     │
     ├─ procesarDatos(jsonData)                                (:348-412)
     │    Agrupar por estación (IdEstacion / EstacionId_1)
     │    Extraer folios de: Folio_1 · Folio_2 · FolioTotales (split por coma)
     │    limpiarFolio(f)   quita envoltorio ="…"               (:415-422)
     │    replaceDigit(f)    trunca el último carácter           (:424-431)
     │    Acumular en Set por estación
     │
     ├─ obtenerMetadataEstaciones(ids)                          (:434-476)
     │    POST /Station/GetStationsMetadata   [Body: List<int>]
     │      └─ SELECT … FROM EstacionesMaestras WHERE IdEstacion IN @ids
     │    Respuesta: [{ idEstacion, ls, cr, nombre, … }]
     │
     └─ addToBulk(ls, nombre, cr, folios)  →  una fila en #selectedTable  (:164-189)
          Cada fila tiene un <textarea> editable con los folios

FASE 3 — Procesamiento masivo
  Click "PROCESAR TODAS"  →  startProcessing()                        (:283-286)
     │
     ├─ obtenerDatosDeFilas()  →  List<StationFolioDTO>{LS,Nombre,CR,FoliosCsv}  (:192-207)
     ├─ mostrar #loader + #progressBar
     │
     └─ POST /Station/ProcessBulk   [Body: List<StationFolioDTO>]
           └─ IStationManager.ExecuteBulkSearch(request, user)        (StationManager:290)
                → searchId = Guid.NewGuid()
                → lanza N tasks con SemaphoreSlim(15)  [fire-and-forget]
                → retorna searchId
        ◄── Ok({ searchId })                                          (:172)

FASE 4 — Polling de progreso (cada 1 segundo)
  GET /Station/GetProgress?searchId={searchId}                         (:155-162)
     └─ IStationManager.GetProgress(searchId)              (StationManager:258-288)
          current/total/percentage  ← ProgressTracker[searchId]  (en memoria)
          failedList                ← SELECT LS FROM BulkSearchErrors WHERE SearchId=@id
     ◄── Ok({ current, total, percentage, failedList })
        │
        ├─ Actualizar #progressBar width y #progressStatus
        ├─ Filas con error → .table-danger + badge "Error" + botón "Reintentar"
        │     retrySingle(ls)  →  re-añade la fila a la tabla y re-procesa
        └─ percentage >= 100 → limpiar filas exitosas (fadeOut), ocultar #loader

FASE 5 — Resultados
  GET  /Station/ObtenerConteoResultados → { total }   habilita/deshabilita botones
  GET  /Station/DownloadExcel
        └─ ExportResultsToExcel()                     (StationManager:433-466)
             SELECT … FROM LocalBulkSearchResults ORDER BY Estacion, Created DESC
             → XLWorkbook + InsertTable(results)
             → formatos: col 4 = "$ #,##0.00" · col 11 = "dd/MM/yyyy HH:mm:ss"
             → MemoryStream.ToArray()  →  File(.xlsx)
        ◄── File(Reporte_Masivo_{yyyyMMdd_HHmm}.xlsx)
  POST /Station/EliminarResultados
        └─ TRUNCATE TABLE LocalBulkSearchResults; TRUNCATE TABLE BulkSearchErrors
```

### 12.3 Flujo C — Recibos, corrección masiva de remisiones y evidencias

```
PASO 1 — Obtener recibos
  Click "Ver Recibos" (o acepta el redirect de GetReceipts)
  GET /Station/GetReceipts?ls={ls}&receiptIds={csv_opcional}
     └─ IStationManager.GetReceipts(ls, receiptIds)        (StationManager:73-151)
          1. Validar ls:  char.IsLetterOrDigit(c) || c == '_'        (:76-77)
          2. SELECT … FROM [{ls}].gaxpos.Purchase.Receipt R
                    INNER JOIN [{ls}].gaxpos.Catalog.RecordType RT …
                    [WHERE R.ReceiptId IN @Ids]  ORDER BY R.ReceiptId DESC
          3. SELECT DISTINCT POId FROM CorrectionHistory WHERE Instance = @ls
          4. item.IsProcessed = true  si el POId está en ese conjunto
     ◄── View("ViewReceipts", IEnumerable<ReceiptViewModel>)
        #receiptsTable (DataTables, pageLength 50, orden [[1,"asc"]])
        Checkbox deshabilitado para filas con IsProcessed

PASO 2 — Seleccionar POs y cargar detalle
  Usuario marca checkboxes → selectAll / refreshPODetails()            (:142-203)
     └─ POST /Station/GetPODetails   [Body: { Instance, POIds }]      (:63-76)
          └─ SELECT … FROM [{ls}].gaxpos.Purchase.PO WHERE POId IN @Ids
          ◄── Json(poDetails)
     └─ renderPOTable(remissions)  →  #poDetailsTable                 (:204-219)
        Muestra POId · NumOC · Status · Remisión Actual · Nueva Remisión

PASO 3 — Pegar remisiones (modal Swal)
  Click "Aplicar Remisiones de Excel"                                 (:221-261)
     └─ Swal con textarea → el usuario pega el listado
        Validar:  remisiones.length === currentPOData.length
        renderPOTable(remisiones)  →  previsualiza antes de guardar
        Swal confirmación

PASO 4 — Captura ANTES
  captureAndSave("ANTES_DE_ACTUALIZAR")                               (:263-289)
     ├─ html2canvas(#containerPODetails, { scale: 2 })  →  dataURL
     └─ POST /Station/SaveEvidence                                   (:113-151)
          Body: { Instance, Moment: "ANTES_DE_ACTUALIZAR", ImageData }
          ├─ Strip prefijo "data:image/png;base64,"
          ├─ Convert.FromBase64String(…)
          ├─ Path.Combine("C:\AppReceiptsEvidenceStations", request.Instance)
          ├─ Directory.CreateDirectory(path)
          ├─ nombre = $"{DateTime.Now:yyyyMMdd_HHmmss}_{request.Moment}.png"
          └─ File.WriteAllBytes(…)
     ◄── Ok()

PASO 5 — Aplicar cambios
  Swal loading                                                     (:296-301)
  POST /Station/ProcessFinalUpdate   [Body: BulkRemissionUpdateDTO] (:79-110)
     Body: { Instance, POIds, NewRemission: "R1,R2,…", OldRemission: "R0,R1,…" }
     │
     ├─ Construir List<RemissionPair> con .Zip(POIds, newRems)              (:88-91)
     │   ★ Zip TRUNCA silenciosamente si las longitudes no coinciden
     │
     ├─ IStationManager.UpdateSpecificRemissions(ls, updateResult)  (StationManager:177-214)
     │    UPDATE [{ls}].gaxpos.Purchase.PO SET Remission=@Remission WHERE POId=@POId
     │    ★ Bucle secuencial, N round-trips, SIN transacción global
     │    ★ Sin validación de ls
     │
     └─ IStationManager.SaveHistory(history)                     (StationManager:217-256)
          using conn = LocalConnection
          using tx   = conn.BeginTransaction()        ← única transacción explícita
          for cada poId:
            INSERT INTO CorrectionHistory (Instance, POId, OldRemission,
                                           NewRemission, AppliedAt, AppliedBy)
            AppliedBy = "Admin"   ← constante, ignora el usuario autenticado
          tx.Commit()   /   catch → tx.Rollback()
     ◄── Ok({ success: true, message: … })

PASO 6 — Captura DESPUÉS
  Actualizar currentPOData con las remisiones nuevas
  captureAndSave("DESPUES_DE_ACTUALIZAR")                    (mismo mecanismo que Paso 4)
  Swal éxito  →  location.reload()                                  (:310-337)
```

### 12.4 Flujo D — Transacciones procesadas (Excel → preview → Hangfire)

```
FASE 1 — Cargar y previsualizar
  Usuario selecciona .xlsx  →  #excelInput  →  "Cargar y previsualizar"
     └─ FormData { archivo } → POST /TransaccionesProcesadas/CargarExcel   (:26-42)
          └─ ITransaccionesProcesadasManager.CargarExcel(IFormFile)   (Manager:31-75)
               1. Validar archivo != null && Length > 0                    (:35-39)
               2. Validar extensión == ".xlsx" (ToLowerInvariant)         (:41-46)
               3. using var stream = archivo.OpenReadStream()
               4. _excelReader.Leer(stream)                (ExcelTransaccionesReader)
                    ├─ XLWorkbook(stream)
                    ├─ hoja = Worksheets(1)   ← siempre la primera
                    ├─ lastColumn = hoja.LastColumnUsed()?.ColumnNumber() ?? 0
                    │     0 → Errores.Add("El archivo Excel está vacío.")
                    ├─ headers = fila 1 → Dictionary<OrdinalIgnoreCase>
                    ├─ faltantes = ColumnasRequeridas − headers
                    │     alguno → Errores.Add("Faltan las columnas requeridas: …")
                    ├─ lastRow = hoja.LastRowUsed()?.RowNumber() ?? 1
                    ├─ for r = 2 .. lastRow:
                    │     leer 5 celdas con LeerCeldaComoTexto
                    │     si las 5 vacías → continue (fila en blanco)
                    │     si no → FilaExcelTransaccionDTO
                    └─ return ExcelLecturaDTO{ Filas, Errores, Success = Errores.Count==0 }

                    LeerCeldaComoTexto(celda):
                      IsNumber → double numero
                        IsNaN/IsInfinity → ""
                        numero == Truncate(numero) → numero.ToString("0", InvariantCulture)
                                                    ★ evita notación científica (1e15 → "1000000000000000")
                        otro → numero.ToString(InvariantCulture)
                      otro → celda.GetString()?.Trim() ?? ""

               5. ObtenerMapaEstaciones()  [Dapper]              (Manager:141-156)
                    SELECT IdEstacion, LS FROM EstacionesMaestras
                    → Dictionary<int,string>
               6. _excelReader.Transformar(lectura.Filas, mapaLS)  (Reader:120-153)
                    foreach fila:
                      if (!int.TryParse(fila.EstacionId.Trim(), out idEstacion)) continue;
                          ★ DESCARTA la fila sin registrar el motivo
                      folios = [Folio1?] + [Folio2?]   (solo no-whitespace, Trim)
                      ls     = mapaLS.TryGetValue(idEstacion) ?? ""
                      → TransaccionProcesadaDTO{ IdEstacion, CR, LS, Nombre,
                                                 FoliosTotales = string.Join(",", folios) }
               7. _excelReader.AgruparPorEstacion(resultado.Filas)  (Reader:156-213)
                    GroupBy(f => f.IdEstacion)
                      CR/LS/Nombre  ← del PRIMER elemento del grupo
                      folios = SelectMany(Split(',')).Where(no vacío)
                                          .Distinct(OrdinalIgnoreCase)
                      TotalFolios   = folios.Count          ★ cuenta ÚNICOS
                      FoliosMuestra = string.Join(", ", folios.Take(3))
                    OrderBy(Nombre).ThenBy(IdEstacion)
               8. resultado.Success = true
          ◄── Json(ExcelCargaResultDTO{ Success, Errores, Filas, Resumen })

     JS: guardar filasPreview = result.filas                      (Index.cshtml:151)
         renderizarPreview(result.resumen)                         (:164-183)
         → #previewCard visible con tabla por estación
         → resumen: totalEstaciones + totalFoliosPreview
         → habilitar #btnGuardar

FASE 2 — Confirmar e encolar
  Click "Confirmar e insertar" → guardar()                        (:203-255)
     ├─ Validar filasPreview.length > 0
     ├─ Mostrar #loader + #progressBar
     └─ POST /TransaccionesProcesadas/Guardar   [Body: List<TransaccionProcesadaDTO>]
          └─ ITransaccionesProcesadasManager.EncolarGuardado(items)  (Manager:77-91)
               if items vacío → return string.Empty
               jobId = Guid.NewGuid()
               TransaccionesProcesadasJob.Inicializar(jobId, lista.Count)
                  _progreso[jobId] = { Current=0, Total=N, Estado="En cola" }
               BackgroundJob.Enqueue<TransaccionesProcesadasJob>(j => j.Procesar(jobId, lista))
                  → INSERT en HangFire.Job + HangFire.JobParameter
               return jobId.ToString()
     ◄── Ok({ success: true, jobId })

FASE 3 — Ejecución del job (proceso del worker de Hangfire)
  TransaccionesProcesadasJob.Procesar(Guid jobId, List<TransaccionProcesadaDTO> items)
     [AutomaticRetry(Attempts = 3)]
     │
     ├─ _progreso[jobId] = { 0, N, "Procesando" }                     (:30)
     ├─ using scope = _scopeFactory.CreateScope()                     (:32)
     ├─ db = scope.GetRequiredService<AppDbContext>()                 (:33)
     ├─ const int batchSize = 500                                      (:35)
     │
     ├─ foreach (lote in items.Chunk(500)):                           (:38)
     │     entidades = lote.Select(dto → new TransaccionProcesada {
     │                     IdEstacion, CR, LS, Nombre, FoliosTotales })
     │                     ★ FechaCarga NO se asigna → DEFAULT GETDATE()
     │     db.TransaccionesProcesadas.AddRange(entidades)             (:49)
     │     await db.SaveChangesAsync()      ← transacción implícita POR LOTE  (:50)
     │     db.ChangeTracker.Clear()         ← libera memoria entre lotes (:51)
     │     procesados += entidades.Count
     │     _progreso[jobId] = { procesados, N, "Procesando" }         (:54)
     │
     ├─ _progreso[jobId] = { procesados, N, "Completado" }            (:57)
     └─ catch: _progreso[jobId] = { …, "Error", Mensaje = ex.Message }; throw   (:59-69)
               ★ El throw activa el reintento de Hangfire (hasta 3)
               ★ Cada reintento REINSERTA las filas ya confirmadas (no hay idempotencia)

FASE 4 — Polling de progreso (cada 1.5 segundos)
  setInterval(1500) → GET /TransaccionesProcesadas/GetProgreso?jobId={jobId}   (:71-75)
     └─ ITransaccionesProcesadasManager.ObtenerProgreso(jobId)      (Manager:93-96)
          → TransaccionesProcesadasJob.ObtenerProgreso(jobId)        (Job:72-87)
               if (!_progreso.TryGetValue(jobId, out p))
                 return { current:0, total:0, percentage:0, estado:"Desconocido", mensaje:"" }
               return { current, total, percentage = (int)((double)current/total*100),
                        estado, mensaje }
     ◄── Ok({ current, total, percentage, estado, mensaje })
        │
        ├─ estado == "Completado" → clearInterval, ocultar #loader, reset UI,
        │                            habilitar Descargar/Eliminar, Swal éxito
        └─ estado == "Error"      → Swal con progress.mensaje

FASE 5 — Post-proceso
  GET  /TransaccionesProcesadas/DescargarCsv
        └─ ExportarCsv()                                (Manager:108-138)
             datos = OrderBy(d => d.Id).ToListAsync()   ← carga TODA la tabla en memoria
             header = "Id,IdEstacion,CR,LS,Nombre,FoliosTotales,FechaCarga"
             foreach d:
                 CsvHelper.Escapar(d.Id)  ·  Escapar(d.IdEstacion)  ·  Escapar(d.CR)
                 Escapar(d.LS)  ·  Escapar(d.Nombre)
                 EscaparComoTexto(d.FoliosTotales)   ← envuelve en ="…" para forzar texto
                 Escapar(d.FechaCarga.ToString("yyyy-MM-dd HH:mm:ss"))
             prepend BOM UTF-8 (Buffer.BlockCopy con Encoding.UTF8.GetPreamble())
        ◄── File(bytes, "text/csv; charset=utf-8", "TransaccionesProcesadas_{timestamp}.csv")

  POST /TransaccionesProcesadas/Eliminar
        └─ ExecuteSqlRawAsync("TRUNCATE TABLE [dbo].[TransaccionesProcesadas]")
```

### 12.5 Flujo E — Auditorías y exportación a PDF

```
  Click "Auditorías" (fila de estación)
  GET /Audit/GetAudits?ls={ls}
     └─ IAuditManager.GetAudits(ls)                            (AuditManager:28-39)
          SELECT AuditId, StationId, StatusName, Folio, Comments,
                 MotiveAuditAdjustmentName, AuditType, StartDate, EndDate
          FROM [{station}].[GAXPOS].[Inventory].[Audit] ORDER BY Created DESC
     ◄── View("Index", List<AuditDTO>)  +  ViewBag.Station = ls
        #auditTable (DataTables, pageLength 10, orden [[0,"desc"]], idioma ES)
     ★ Nota: el SELECT trae "StationId" pero el DTO define "IdEstacion" → IdEstacion queda 0

  Click "Ver Detalle"
  GET /Audit/AuditDetail?ls={ls}&auditId={id}
     ├─ GetproductsByAudit(ls, auditId)                        (AuditManager:43-63)
     │    SELECT ProductAuditInventoryId, ProductId, ProductName,
     │           TheoreticalInventory, ActualInventory, InventoryDifference
     │    FROM [{station}].[GAXPOS].[Inventory].[ProductAuditInventory]
     │    WHERE AuditId = @auditId
     │    ★ El DTO tiene 11 propiedades; el SELECT solo trae 6
     │      → ProductPrice, AuditId, TheoreticalAmount, ActualAmount, DifferenceAmount = 0
     │
     ├─ ids = inventory.Select(x => x.ProductAuditInventoryId).Distinct().ToList()
     ├─ GetAuditDetails(ls, ids)                               (AuditManager:67-96)
     │    var idsString = string.Join(",", ids);   ← calculada y NUNCA usada (:71)
     │    SELECT pd.ProductAuditInventoryDetailId, pd.LocationId, l.LocationName,
     │           pd.TheoreticalInventory, pd.ActualInventory, pd.ProductAuditInventoryId
     │    FROM [{station}].[GAXPOS].[Inventory].[ProductAuditInventoryDetail] pd
     │    INNER JOIN [{station}].[GAXPOS].[Inventory].[Location] l ON pd.LocationId = l.LocationId
     │    WHERE pd.ProductAuditInventoryId IN @Ids
     │    ORDER BY pd.ProductAuditInventoryId DESC
     │
     └─ AuditDetailViewModel{ Station, AuditId, Inventory, Details }
     ◄── View("AuditDetail", model)
        #inventoryTable + #detailTable (DataTables)
        #inventoryChart (Chart.js): labels = ProductName, data = InventoryDifference
        Badges: Correcto / Sobrante / Faltante según signo de InventoryDifference

  Click "Exportar PDF"
  GET /Audit/ExportPdf?auditId={id}&station={station}
     └─ IAuditManager.GetFullAudit(station, auditId)            (AuditManager:99-135)
          1. inventory = await GetproductsByAudit(…)
          2. si null/vacío → LogWarning + VM con listas vacías
          3. ids = inventory.Select(ProductAuditInventoryId).Distinct()
          4. details = await GetAuditDetails(station, ids)
          5. return AuditDetailViewModel{ Station, AuditId, Inventory, Details }
     ◄── ViewAsPdf("PdfReport", model) {
              FileName = $"Audit_{auditId}.pdf",
              PageSize = A4, PageOrientation = Landscape,
              Margins = { left=5, right=5, top=5, bottom=5 } }
        │
        └─ Rotativa renderiza Views/Audit/PdfReport.cshtml  (Layout=null)
             Tabla con multi-header (rowspan 2 + colspan 2 por Rack 1-4 + General)
             Estilos inline (colores ok/warning/danger por diferencia)
             → invoca wwwroot/Rotativa/wkhtmltopdf.exe
             → FileResult PDF
```

### 12.6 Flujo F — Servicios y transacciones

```
  SERVICIOS
    GET  /Service/Index
      userId = int.Parse(User.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value)
      → ServiceManager.GetallServices(userId)   [EF, síncrono]
      → View(List<ServiceViewModel>)
    POST /Service/CreateNewService   Body: { Name, Type }
      → ServiceManager.SaveNewService(sm)
           sm.UserId = 1;    ← HARDCODED  ("Temporary userId = 1")   (ServiceManager:38)
           Add + SaveChanges()
      → RedirectToAction("Index") + TempData["Message"/"MessageType"]
    POST /Service/Updateservice      Body: { ServiceId, Name, Type }
      → ServiceManager.UpdateService(sm)   → Redirect + TempData
    POST /Service/Delete             [ValidateAntiForgeryToken]  id
      → ServiceManager.DelteTask(id)      (nombre con typo)
           if null → throw new Exception()  (sin mensaje)
           Remove + SaveChanges()

  TRANSACCIONES
    GET  /Transaction/Index               → formulario (Type, Service, Comments, Date, Amount)
      #cboTypeService onchange → ShowServices()   (:63-83)
        GET /Transaction/GetServicesByType?type=income
          → ServiceManager.GetByType(userId, type)   [EF, síncrono]
          → llena #cboService (Select2)
    POST /Transaction/SaveNewTransaction  [Body: TransactionDTO]
      if (!ModelState.IsValid) → BadRequest({ success:false, message:"Invalid data.", errors })
      → ITransactionManager.SaveNew(dto)   [EF, síncrono]
           map DTO → Entity.Transaction
           Add + SaveChanges()
           LogInformation("Transaction saved successfully for UserId:{…}, ServiceId:{…}, Amount:{…}")
           result > 0 → Ok({ success:true, message:"Transaction saved…" })
           catch → LogError + return -1 → BadRequest({ success:false, … })
    GET  /Transaction/HistoryTransactions → View("History")
    GET  /Transaction/GetHistoryTransactions?startDate=&endDate=
      if fechas null → rango del mes actual (día 1 → último día)
      → ITransactionManager.GetTransactionsHistory(start, end, userId)   [EF, async]
           Where(t.UserId == userId && t.Date >= start && t.Date <= end)
           Proyección: HistoryTransactionDTO{
              Date  = t.Date.ToString("dd/MM/yyyy"),
              Month = t.Date.ToString("MMMM"),      ★ depende de CurrentCulture del servidor
              TypeService = t.Service.Type, Service = t.Service.Name,
              Amount = t.TotalAmount }
           ★ ToList() SÍNCRONO dentro de un método async
      ◄── Ok({ data })
```

---

## 13. Procesamiento en background (Hangfire)

### 13.1 Configuración
```csharp
// Program.cs:65-74
var hangfireConnectionString = builder.Configuration.GetConnectionString("LocalDb");
builder.Services.AddHangfire(config =>
{
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180);
    config.UseSimpleAssemblyNameTypeSerializer();
    config.UseRecommendedSerializerSettings();
    config.UseSqlServerStorage(hangfireConnectionString);   // misma BD que la app
});
builder.Services.AddHangfireServer();
builder.Services.AddSingleton<TransaccionesProcesadasJob>();
```

**Características de la configuración:**
- El storage de Hangfire comparte la base de datos con la aplicación (`LocalDb`), en el esquema `[HangFire]`.
- `AddHangfireServer()` se invoca **sin opciones**: 20 workers por defecto, sin `Queues`, sin `ShutdownTimeout`, sin `SchedulePollingInterval` personalizados.
- El job se registra como **Singleton** e inyecta `IServiceScopeFactory` para crear un scope propio por ejecución (necesario porque `AppDbContext` es Scoped).

### 13.2 El job

**Clase:** `Managers/TransaccionesProcesadasJob.cs` (97 líneas), namespace `MoneyFlow.Managers`.
**Atributos:** `[AutomaticRetry(Attempts = 3)]` sobre `Procesar`.
**Estado interno:** `static readonly Dictionary<Guid, ProgresoCarga> _progreso` (`:13`). `ProgresoCarga` es una clase privada anidada (`:89-95`) con `int Current`, `int Total`, `string Estado`, `string Mensaje`.

| Método | Línea | Estática | Firma |
|---|---|---|---|
| `Inicializar` | 20-23 | ✅ | `static void Inicializar(Guid jobId, int total)` |
| `Procesar` | 25-70 | ❌ | `[AutomaticRetry(Attempts=3)] async Task Procesar(Guid jobId, List<TransaccionProcesadaDTO> items)` |
| `ObtenerProgreso` | 72-87 | ✅ | `static object ObtenerProgreso(Guid jobId)` |

**Características observables del diseño:**
- **Fire-and-forget, no recurrente.** Se usa `BackgroundJob.Enqueue`, nunca `RecurringJob`. No hay ningún `IRecurringJobManager` en el proyecto.
- **El DTO completo viaja serializado** al Hangfire storage (`List<TransaccionProcesadaDTO>` como parámetro del job), con `UseSimpleAssemblyNameTypeSerializer`.
- **Progreso en memoria del proceso:** el diccionario es `static`, se pierde al reiniciar la app y no se comparte entre instancias si la aplicación escala horizontalmente.
- **`Dictionary` no thread-safe:** se escribe desde los hilos del worker y se lee desde los hilos de los requests HTTP de polling.
- **Transacción por lote, no global:** cada `SaveChangesAsync()` confirma 500 filas. Si el lote 3 de 5 falla, los lotes 1-2 quedan persistidos y el reintento de Hangfire los vuelve a insertar (no hay clave natural ni `UNIQUE` que lo impida).

### 13.3 Cycle de vida de un job
```
Encolado      BackgroundJob.Enqueue<T>(expr)  →  INSERT HangFire.Job + JobParameter
             TransaccionesProcesadasJob.Inicializar(jobId, N)  →  _progreso[jobId] = "En cola"
Recuperación  El worker de Hangfire (HostedService) sondea la cola
Ejecución     Procesar() → scope propio → lotes de 500 → SaveChangesAsync por lote
Reintento     [AutomaticRetry(Attempts=3)] con backoff exponencial ante excepción
Estado        "Procesando" → "Completado" | "Error"
Consulta      GetProgreso(jobId) lee el diccionario estático
Limpieza      Ninguna automática (el job queda en el historial del dashboard)
```

---

## 14. Capa de negocio (Managers)

### 14.1 Inventario

| Clase | Líneas | Interfaz | Acceso a datos | DI |
|---|---|---|---|---|
| `StationManager` | 506 | `IStationManager` | **Dapper** a `oxxogas`, linked servers y local. **No recibe `AppDbContext`** | Scoped |
| `TransaccionesProcesadasManager` | 164 | `ITransaccionesProcesadasManager` | **Híbrido**: EF + `SqlConnection` manual | Scoped |
| `ServiceManager` | 141 | *(ninguna)* | EF Core. **Primary constructor** con `AppDbContext` | Scoped (tipo concreto) |
| `AuditManager` | 137 | `IAuditManager` | **Dapper** a `StationsDb` (100 % remota). No recibe `AppDbContext` | Scoped |
| `UserManager` | 126 | `IUserManager` | EF Core + `IPasswordHasher<User>` | Scoped |
| `TransaccionesProcesadasJob` | 97 | *(ninguna)* | EF Core (job Hangfire) | Singleton |
| `TransactionManager` | 70 | `ITransactionManager` | EF Core | Scoped |
| `LogAnalyzerManager` | 7 | *(ninguna, clase vacía)* | — | **No registrada** |

### 14.2 Interfaces
| Interfaz | Líneas | Métodos | Implementada |
|---|---|---|---|
| `IStationManager` | 39 | 11 (`SearchStations`, `GetReceipts`, `GetDetailsFromPO`, `UpdateSpecificRemissions`, `SaveHistory`, `ExecuteBulkSearch`, `ExportResultsToExcel`, `GetProgress`, `ObtenerConteoResultados`, `TruncarResultados`, `GetStationMetadata`) | ✅ |
| `ITransaccionesProcesadasManager` | 27 | 6 (`CargarExcel`, `EncolarGuardado`, `ObtenerProgreso`, `ObtenerConteo`, `Truncar`, `ExportarCsv`) | ✅ |
| `IUserManager` | 18 | 4 (`Login`, `LoginShow`, `GetByEmail`, `ValidatePassword`) | ✅ |
| `IAuditManager` | 16 | 4 (`GetAudits`, `GetproductsByAudit`, `GetAuditDetails`, `GetFullAudit`) | ✅ |
| `ITransactionManager` | 11 | 2 (`SaveNew`, `GetTransactionsHistory`) | ✅ |
| `ILogAnalyzerManager` | 6 | **0** (interfaz vacía) | ❌ |

**Notas de contrato:**
- `ITransaccionesProcesadasManager.CargarExcel(IFormFile)` depende de `Microsoft.AspNetCore.Http` — la interfaz de negocio expone un tipo de la capa HTTP.
- `IStationManager.GetProgress`, `ObtenerConteoResultados` y `TruncarResultados` son **síncronos** y devuelven `int`/`object`/`void`.
- `ITransaccionesProcesadasManager.EncolarGuardado` y `ObtenerProgreso` son **síncronos** y devuelven `string`/`object`.
- `ITransactionManager.SaveNew` devuelve `int` sincrónicamente, con `-1` como indicador de error.
- `IUserManager` declara `Task<UserViewModel>` sin anotación de nulabilidad, pero `UserManager.ValidatePassword` devuelve `UserViewModel?`.

### 14.3 `ExcelTransaccionesReader` (213 líneas)

Clase **instanciable con `new()`** (sin ctor declarado, sin dependencias, sin interfaces). Namespace `MoneyFlow.Utilities`.

**Constantes de columnas requeridas:**
```csharp
public const string ColumnaEstacionId = "EstacionId_1";
public const string ColumnaCR          = "CR_1";
public const string ColumnaEstacion    = "Estacion_1";
public const string ColumnaFolio1      = "Folio_1";
public const string ColumnaFolio2      = "Folio_2";
public string[] ColumnasRequeridas => new[] { … };   // property, crea array nuevo en cada acceso
```

**`ExcelLecturaDTO Leer(Stream stream)` (`:20-111`)** — ver desglose en la Fase 1 del flujo D (§12.4).
Casos borde: hoja vacía → error; sin filas de datos → `Success=true` con 0 filas; fila con las 5 celdas vacías → se salta; fila parcialmente vacía → se conserva.

**`List<TransaccionProcesadaDTO> Transformar(IEnumerable<FilaExcelTransaccionDTO> filas, IDictionary<int,string> mapaLS)` (`:120-153`)**
- Descarta filas cuyo `EstacionId` no parsea como `int` (usa `int.TryParse`, **sin registrar el motivo**).
- Toma hasta 2 folios por fila (`Folio1`, `Folio2`). Si ambos vacíos, la fila se conserva con `FoliosTotales = ""`.
- `LS` se resuelve desde el mapa; clave ausente o `mapaLS == null` → `string.Empty`.
- `FoliosTotales = string.Join(",", folios)` — pseudo-CSV sin espacios.

**`List<ResumenEstacionDTO> AgruparPorEstacion(IEnumerable<TransaccionProcesadaDTO> filas)` (`:156-213`)**
- `GroupBy(f => f.IdEstacion)`; `CR`/`LS`/`Nombre` del primer elemento del grupo.
- Folios: `SelectMany(Split(',', RemoveEmptyEntries | TrimEntries))` → `Where(no whitespace)` → `Distinct(OrdinalIgnoreCase)`.
- `TotalFolios` = conteo de folios **únicos** (puede diferir del número de renglones).
- `FoliosMuestra` = `string.Join(", ", folios.Take(3))`.
- Orden final: `OrderBy(Nombre).ThenBy(IdEstacion)`.

### 14.4 `CsvHelper` (31 líneas) — clase estática

**`static string Escapar(string value)`**
```
null o ""            → ""
contiene ',' '"' '\n' → "\"" + value.Replace("\"","\"\"") + "\""
resto                 → value (sin comillas, sin Trim)
```
Verificado contra tests: `Escapar("a,b") == "\"a,b\""`.

**`static string EscaparComoTexto(string value)`** — trick de Excel para forzar formato texto:
```csharp
var formula = "=\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
return Escapar(formula);
```
El resultado siempre contiene `"`, por lo que siempre pasa por la rama de entrecomillado de `Escapar`. Se usa **únicamente** para `FoliosTotales` en `ExportarCsv` (`TransaccionesProcesadasManager.cs:125`).

### 14.5 Otros Utilities

| Clase | Líneas | Función |
|---|---|---|
| `HangfireAuthorizationFilter` | 17 | `Authorize(DashboardContext) => GetHttpContext()?.User?.Identity?.IsAuthenticated == true` |
| `PasswordService` | 20 | `Hash(string)` / `Verify(string,string)` sobre `PasswordHasher<object>` propio. **Registrado pero su único consumidor (`UserMigrationService`) está deshabilitado** |
| `UserMigrationService` | 39 | `MigrateUsersAsync()`: recorre `User.Where(u => u.Password != null)`, hashea el texto plano a `PasswordHash`, un `SaveChangesAsync()` por usuario, errores a `Console.WriteLine`. Registro y ejecución **comentados** en `Program.cs` |

---

## 15. Contratos (DTOs y ViewModels)

### 15.1 DTOs (`DTOs/`, 13 archivos)

| DTO | Propiedades | Productor | Consumidor |
|---|---|---|---|
| `TransaccionProcesadaDTO` | `IdEstacion` (int), `CR`, `LS`, `Nombre`, `FoliosTotales` (CSV) | `ExcelTransaccionesReader.Transformar` | `POST /TransaccionesProcesadas/Guardar` (JSON), `TransaccionesProcesadasJob` |
| `ResumenEstacionDTO` | `IdEstacion`, `CR`, `LS`, `Nombre`, `TotalFolios` (int), `FoliosMuestra` (string, máx. 3) | `ExcelTransaccionesReader.AgruparPorEstacion` | `ExcelCargaResultDTO.Resumen` → `TransaccionesProcesadas/Index.cshtml` |
| `ExcelCargaResultDTO` | `Success` (bool, settable), `Errores` (List\<string\>), `Filas` (List\<TransaccionProcesadaDTO\>), `Resumen` (List\<ResumenEstacionDTO\>) | `TransaccionesProcesadasManager.CargarExcel` | `CargarExcel` controller → JS (`result.success`/`errores`/`filas`/`resumen`) |
| `ExcelLecturaDTO` | `Filas` (List\<FilaExcelTransaccionDTO\>), `Errores` (List\<string\>), `Success => Errores.Count == 0` (**calculada, read-only**) | `ExcelTransaccionesReader.Leer` | `TransaccionesProcesadasManager` |
| `FilaExcelTransaccionDTO` | `EstacionId`, `CR`, `Estacion`, `Folio1`, `Folio2` (todos string) | `ExcelTransaccionesReader.Leer` | `Transformar` |
| `StationFolioDTO` | `LS`, `Nombre`, `CR`, `FoliosCsv` | JS (`obtenerDatosDeFilas`) | `POST /Station/ProcessBulk` → `ExecuteBulkSearch`. **CR se ignora** (se recalcula en SQL con `@CR_Remote`) |
| `BulkSearchResultDTO` | `Folio`, `OrderId` (Guid), `Tipo`, `Total` (decimal), `NombreEmpleado`, `EstadoEmpleado`, `EmpleadoEstacion`, `RoleName`, `CR`, `Estacion`, `Created` (DateTime) | `StationManager.ExportResultsToExcel` (Dapper) | ClosedXML `InsertTable`. **No incluye `SearchId`/`UserExecution`** aunque la tabla sí → el export mezcla resultados de todas las búsquedas |
| `AuditDTO` | `AuditId`, `IdEstacion`, `StatusName`, `Folio`, `Comments`, `MotiveAuditAdjustmentName`, `AuditType`, `StartDate?`, `EndDate?` | `AuditManager.GetAudits` | `Audit/Index.cshtml` |
| `ProductAuditDTO` | `ProductAuditInventoryId`, `ProductId`, `ProductName`, `ProductPrice`, `TheoreticalInventory`, `ActualInventory`, `InventoryDifference`, `TheoreticalAmount`, `ActualAmount`, `DifferenceAmount`, `AuditId` | `AuditManager.GetproductsByAudit` | `Audit/AuditDetail.cshtml`, `PdfReport.cshtml` |
| `ProductAuditDetailDTO` | `ProductAuditInventoryDetailId`, `LocationId`, `LocationName`, `TheoreticalInventory`, `ActualInventory`, `ProductAuditInventoryId` | `AuditManager.GetAuditDetails` | `Audit/AuditDetail.cshtml` |
| `HistoryTransactionDTO` | `Date` ("dd/MM/yyyy"), `Month` ("MMMM"), `TypeService`, `Service`, `Amount` | `TransactionManager.GetTransactionsHistory` (EF) | `Transaction/History.cshtml` (DataTables) |
| `TransactionDTO` | `ServiceId`, `UserId`, `Comment`, `Date` (DateOnly), `TotalAmount` | JS | `POST /Transaction/SaveNewTransaction`. **Único DTO con DataAnnotations**: `[Required]`×4 + `[Range(0.01, double.MaxValue)]` en `TotalAmount` |
| `LogSearchCriteriaDTO` | `record` posicional: `StartDate?`, `EndDate?`, `LogLEvel?`, `SourceContains?`, `MEssageContains?`, `CorrelationId?`, `DispatchId?`, `ErrorCode?` | — | — **Huérfano.** Único miembro del módulo de análisis de logs revertido (commit `6321742`) |

### 15.2 ViewModels (`Models/`, 8 archivos / 12 clases)

| ViewModel | Ubicación | Propiedades | Notas |
|---|---|---|---|
| `LoginViewModel` | `Models/LoginViewModel.cs:12` | `Email`, `Password` | Sin DataAnnotations |
| `UserViewModel` | `Models/UserViewModel.cs:11` | `UserId`, `Name`, `Email`, `Password`, `RepeatPassword` | Retorno de `IUserManager`. `Password`/`RepeatPassword` nunca se llenan |
| `ServiceViewModel` | `Models/ServiceViewModel.cs:9` | `ServiceId`, `UserId`, `Name`, `Type` | Sin DataAnnotations → `ModelState.IsValid` siempre true en Create/Update |
| `StationViewModel` | `Models/StationViewModel.cs:12` | `IdEstacion`, `CR`, `LS`, `Nombre`, `Estacion`, `Activo` (bool) | Destino de los 3 queries de estaciones |
| `ReceiptViewModel` | `Models/ReceiptViewModel.cs:23` | 17 props: `ReceiptId`, `RecordTypeId`, `Description`, `Quantity`, `ReceiptStatusId` (byte), `StatusId`, `POId`, `Notes`, `IsFuel`, `CreatedBy`, `Created`, `LastModifiedBy`, `LastModified`, `CancellationDate`, `InventoryAssignationType`, `IsProcessed` | `LastModified` y `CancellationDate` son `DateTime` no-nullable |
| `POVViewModel` | `Models/POVViewModel.cs:73` | 22 props del SELECT de `Purchase.PO` | Todo nullable salvo `POId` y `Created`. Diseñado para bases remotas heterogéneas |
| `BulkRemissionUpdateDTO` | `Models/POVViewModel.cs:45` | `Instance`, `POIds` (List\<int\>), `NewRemission` (CSV), `OldRemission?` | Contrato JSON de `GetPODetails` y `ProcessFinalUpdate`. Una versión anterior (con `List<RemissionPair> Updates`) está comentada en `:39-43` |
| `RemissionPair` | `Models/POVViewModel.cs:33` | `POId` (int), `Remission` | Par individual para la actualización |
| `CorrectionHistory` | `Models/POVViewModel.cs:54` | `Id`, `Instance`, `POIds` (List\<int\>), `POId` (int), `OldRemission`, `NewRemission`, `AppliedAt`, `AppliedBy` | **Vive en `Models/`, no en `Entities/`** pese a mapear a una tabla |
| `EvidenceRequest` | `Models/POVViewModel.cs:67` | `Instance`, `Moment`, `ImageData` (base64) | `Moment` ∈ {"ANTES_DE_ACTUALIZAR", "DESPUES_DE_ACTUALIZAR"} |
| `AuditDetailViewModel` | `Models/AuditDetailViewModel.cs:13` | `Station`, `AuditId`, `Inventory` (List\<ProductAuditDTO\>), `Details` (List\<ProductAuditDetailDTO\>) | Vista + PDF |
| `ErrorViewModel` | `Models/ErrorViewModel.cs:9` | `RequestId?`, `ShowRequestId` (bool calculada) | Vista `/Home/Error` |

**Dato transversal:** con `Nullable=enable`, la mayoría de los DTOs y ViewModels declaran `string` no-nullable **sin inicializar**, generando warnings CS8618. Solo `LoginViewModel`, `TransactionDTO` y `Service` (entidad) tienen DataAnnotations.

---

## 16. Pruebas

**Proyecto:** `MoneyFlow.Tests/` — xUnit 2.9.2, `Microsoft.NET.Test.Sdk` 17.11.1, `xunit.runner.visualstudio` 2.8.2, `net9.0`. **Sin mocks** (no hay Moq, NSubstitute ni FluentAssertions), sin `EFCore.InMemory`, sin `Testcontainers`, sin cobertura de código configurada.

**Total: 12 tests, todos `[Fact]`, 0 `[Theory]`, 0 `[InlineData]`.**

### `CsvHelperTests` (40 líneas) — 5 tests
| Test | Verifica |
|---|---|
| `Escapar_CitaCamposConComa` | `Escapar("a,b") == "\"a,b\""` |
| `Escapar_NoCitaCamposSimples` | `Escapar("simple") == "simple"` |
| `Escapar_ValorNuloDevuelveVacio` | `Escapar(null) == string.Empty` |
| `EscaparComoTexto_DevuelveFormulaDeTexto` | `EscaparComoTexto("467429920,467429750")` produce el envoltorio `="…"` entrecomillado |
| `EscaparComoTexto_SinComa` | `EscaparComoTexto("467429920")` |

### `ExcelTransaccionesReaderTests` (194 líneas) — 7 tests
Usa ClosedXML real para construir un `MemoryStream`. Instancia `new ExcelTransaccionesReader()`.
| Test | Verifica |
|---|---|
| `Leer_ConColumnasCorrectas_DevuelveFilasYSinErrores` | Columnas válidas → `Success=true`, `Errores` vacío, 2 filas, valores esperados |
| `Leer_SinColumnasRequeridas_DevuelveErrores` | Columnas faltantes → `Success=false`, `Errores` menciona el nombre de la columna |
| `Transformar_ConcatenaFoliosYSeparaPorComa` | `FoliosTotales == "F100,F101"` (sin coma final si `Folio2` vacío) |
| `Transformar_AsignaLSSegunMapaYDejaVacioSiNoExiste` | LS del mapa, o `string.Empty` si la clave no existe |
| `Transformar_IgnoraFilasConIdEstacionInvalido` | De 3 filas (`"abc"`, `"20"`, `""`) sale 1 con `IdEstacion == 20` |
| `Leer_FolioNumericoLargo_NoDevuelveNotacionCientifica` | Número real `1e15` → `"1000000000000000"` (no `"1E+15"`) |
| `AgruparPorEstacion_AgrupaYCuentaFolios` | 2 grupos; `TotalFolios == 3`; `FoliosMuestra` contiene `F100` |

**Cobertura de los tests:** las 2 clases puras más testeadas del proyecto. **No hay tests** para Controllers (8), Managers (7), `PasswordService`, `HangfireAuthorizationFilter`, `UserMigrationService`, ni para las ramas asíncronas (`Procesar`, `ExportarCsv`, `ExecuteBulkSearch`, `MigrateUsersAsync`).

**Nota metodológica:** los tests dependen del `CurrentCulture` del proceso (no lo fijan con `CultureInfo`) y del formato de salida de ClosedXML.

---

## 17. Elementos no conectados al flujo de la aplicación

| Elemento | Ubicación | Estado |
|---|---|---|
| `ILogAnalyzerManager` | `Interfaces/ILogAnalyzerManager.cs` | **Interfaz vacía** (cuerpo `{}`, sin miembros) |
| `LogAnalyzerManager` | `Managers/LogAnalyzerManager.cs` | **Clase vacía** (cuerpo `{}`); no implementa la interfaz anterior; nunca registrada |
| `LogSearchCriteriaDTO` | `DTOs/LogSearchCriteriaDTO.cs` | **Huérfano.** Su única aparición en todo el repositorio es la propia declaración. Restos del commit `78315f7` revertido por `6321742` |
| `UserMigrationService` | `Utilities/UserMigrationService.cs` | Registro comentado (`Program.cs:77`), ejecución al arranque comentada (`Program.cs:110-115`) |
| `PasswordService` | `Utilities/PasswordService.cs` | Registrado en DI (`:78`), consumido **solo** por `UserMigrationService` (deshabilitado). El login real usa `IPasswordHasher<User>` inyectado |
| `Entities/Receipt.cs` | `Entities/Receipt.cs` | **Clase vacía**, sin `DbSet`, no genera tabla. El concepto vive en `Models/ReceiptViewModel.cs` |
| `AccountController.AccessDenied` | Referenciado en `Program.cs:24` | **No existe.** `AccountController` solo expone `Login`. Un 403 redirige a un 404 |
| `jwt:*` (4 claves) | `appsettings.json:9-12` | **No leídas por ningún código.** El paquete `JwtBearer` tampoco se usa |
| `Logging:LogLevel:*` | `appsettings.json:2-7` y `appsettings.Development.json:2-7` | **Inerte.** `UseSerilog()` con configuración en código ignora esta sección |
| `MoneyFlow.styles.css` | Referenciado en `_Layout.cshtml:11` | No existe en `wwwroot/`; lo produce el pipeline de CSS scoped de Razor |
| Enlaces del navbar | `_Layout.cshtml:51,61,65` | `href="#"` ×2 y enlace a `Receipts/Index` (controller inexistente) |
| `ReceiveEvidence` / `EvidenceRequest` | `StationController.SaveEvidence:113` | Funcional, pero la captura depende del lado cliente (`html2canvas`) y escribe en ruta local del servidor |
| Código muerto en consultas | `AuditManager.cs:71` | `var idsString = string.Join(",", ids);` se calcula y nunca se usa (el SQL usa `@Ids` parametrizado) |

---

## 18. Glosario de dominio

| Término | Significado en MoneyFlow |
|---|---|
| **Estación** | Punto de servicio de una gasolinera con su propia instancia de base de datos. Identificada por `IdEstacion` y por el nombre de instancia `LS`. |
| **CR** | *Código de Retail*. Identificador corto de la estación en el sistema heredado. En SQL se obtiene de `GAXPOS.System.Stations.StationCROracle`. |
| **LS** | *Link Server* — nombre de la instancia SQL Server de la estación. Se construye quitando espacios del campo `nombre` de `relacionestaciones`: `REPLACE(RE.nombre,' ','')`. Es el valor que se concatena en `[{LS}].[…]`. |
| **oxxogas** | Base de datos corporativa central de la red (servidor `10.52.21.11`). Contiene el catálogo maestro de estaciones. |
| **GAXPOS** | Sistema POS (Point of Sale) que opera en cada estación. Cada instancia expone bases `gaxpos` y `GAXPOS` con esquemas `Purchase`, `Sale`, `Catalog`, `Inventory`, `Security`, `System`. |
| **Folio** | Número de documento de venta en `Sale.Order.Folio`. Es la clave de búsqueda en el módulo de búsqueda masiva. |
| **PO / Purchase Order** | Orden de compra (`Purchase.PO`). Campos usados: `POId`, `NumOC` (número de orden de compra), `Total`, `Status`, `Remission`, `RemissionDate`. |
| **Remisión** | Identificador de la entrega física asociada a un PO. El módulo de corrección los actualiza masivamente con evidencia gráfica antes/después. |
| **Receipt / Recibo** | Registro de recepción (`Purchase.Receipt`): `ReceiptId`, `RecordTypeId`, `ReceiptStatusId`, `StatusId`, `POId`, `IsFuel`, `InventoryAssignationType`. |
| **RecordType** | Catálogo de tipos de recibo (`Catalog.RecordType`), se une por `RecordTypeId`. |
| **CorrectionHistory** | Tabla **local** de auditoría de correcciones. Columnas: `Instance`, `POId`, `OldRemission`, `NewRemission`, `AppliedAt`, `AppliedBy`. Se usa también para marcar POs ya corregidos en la vista de recibos. |
| **Auditoría de Inventario** | Registro de conteo físico (`Inventory.Audit`) con detalle por producto (`ProductAuditInventory`) y por ubicación (`ProductAuditInventoryDetail` unido a `Location`). |
| **EstacionesMaestras** | Tabla **local** de maestro de estaciones. Provee el mapeo `IdEstacion → LS` y el metadata (`cr`, `nombre`, `ACtiva`) para la carga masiva. |
| **LocalBulkSearchResults** | Tabla **local** que persiste los resultados de búsquedas masivas. Se exporta a Excel y se trunca desde la UI. |
| **BulkSearchErrors** | Tabla **local** que registra las estaciones que fallaron en una búsqueda masiva. Alimenta el `failedList` del polling. |
| **searchId** | `Guid` que identifica una ejecución de búsqueda masiva en `StationManager`. Vive en `ProgressTracker` (memoria). |
| **jobId** | `Guid` que identifica un job de Hangfire en `TransaccionesProcesadasJob`. Vive en `_progreso` (memoria). |
| **Polling** | Patrón de la UI: `setInterval` en el cliente que consulta el avance (`GetProgress` / `GetProgreso`) hasta alcanzar 100 %. |
| **Evidencia (ANTES / DESPUÉS)** | Capturas PNG del estado de la tabla de POs generadas con `html2canvas` antes y después de aplicar una corrección masiva. Se escriben en `C:\AppReceiptsEvidenceStations\{Instance}\`. |
| **MoneyFlowDb** | Base de datos local de la aplicación (`LocalDb`). Alberga tanto el modelo EF como las tablas auxiliares Dapper y el storage de Hangfire. |
| **Bulk Search** | Funcionalidad de búsqueda masiva de folios en múltiples estaciones concurrentes (hasta 15). |
| **FoliosTotales** | Campo de texto que concatena los folios de una estación separados por coma (pseudo-CSV). Se genera en `Transformar`, se cuenta en `AgruparPorEstacion` y se exporta con `CsvHelper.EscaparComoTexto`. |

---

## 19. Inventario de archivos `.cs` del proyecto web

58 archivos (excluyendo `bin/` y `obj/`) · 3 494 líneas.

| Líneas | Archivo |
|---|---|
| 506 | `Managers/StationManager.cs` |
| 227 | `Controllers/StationController.cs` |
| 213 | `Utilities/ExcelTransaccionesReader.cs` |
| 164 | `Managers/TransaccionesProcesadasManager.cs` |
| 160 | `Migrations/20260121230151_FirstMigration.Designer.cs` *(generado)* |
| 157 | `Migrations/AppDbContextModelSnapshot.cs` *(generado)* |
| 141 | `Managers/ServiceManager.cs` |
| 137 | `Managers/AuditManager.cs` |
| 126 | `Managers/UserManager.cs` |
| 124 | `Controllers/TransaccionesProcesadasController.cs` |
| 117 | `Program.cs` |
| 117 | `Controllers/ServiceController.cs` |
| 113 | `Migrations/20260121230151_FirstMigration.cs` *(generado)* |
| 97 | `Managers/TransaccionesProcesadasJob.cs` |
| 94 | `Controllers/AccountController.cs` |
| 89 | `Controllers/TransactionController.cs` |
| 75 | `Context/AppDbContext.cs` |
| 73 | `Models/POVViewModel.cs` *(contiene 5 clases)* |
| 70 | `Managers/TransactionManager.cs` |
| 68 | `Controllers/AuditController.cs` |
| 54 | `Controllers/HomeController.cs` |
| 39 | `Utilities/UserMigrationService.cs` |
| 39 | `Interfaces/IStationManager.cs` |
| 31 | `Utilities/CsvHelper.cs` |
| 27 | `Interfaces/ITransaccionesProcesadasManager.cs` |
| 23 | `Models/ReceiptViewModel.cs` |
| 20 | `Utilities/PasswordService.cs` |
| 19 | `DTOs/TransactionDTO.cs` |
| 19 | `Entities/Service.cs` |
| 18 | `Interfaces/IUserManager.cs` |
| 17 | `DTOs/ProductAuditDTO.cs` |
| 17 | `DTOs/BulkSearchResultDTO.cs` |
| 17 | `Utilities/HangfireAuthorizationFilter.cs` |
| 16 | `DTOs/AuditDTO.cs` |
| 16 | `Entities/Transaction.cs` |
| 16 | `Interfaces/IAuditManager.cs` |
| 14 | `Controllers/SearchController.cs` |
| 14 | `DTOs/LogSearchCriteriaDTO.cs` |
| 13 | `Entities/TransaccionProcesada.cs` |
| 13 | `Entities/User.cs` |
| 13 | `Models/AuditDetailViewModel.cs` |
| 13 | `DTOs/ResumenEstacionDTO.cs` |
| 12 | `Models/StationViewModel.cs` |
| 12 | `Models/LoginViewModel.cs` |
| 12 | `DTOs/HistoryTransactionDTO.cs` |
| 12 | `DTOs/ProductAuditDetailDTO.cs` |
| 11 | `Interfaces/ITransactionManager.cs` |
| 11 | `Models/UserViewModel.cs` |
| 11 | `DTOs/FilaExcelTransaccionDTO.cs` |
| 11 | `DTOs/TransaccionProcesadaDTO.cs` |
| 10 | `DTOs/ExcelCargaResultDTO.cs` |
| 10 | `DTOs/StationFolioDTO.cs` |
| 9 | `DTOs/ExcelLecturaDTO.cs` |
| 9 | `Models/ServiceViewModel.cs` |
| 9 | `Models/ErrorViewModel.cs` |
| 7 | `Managers/LogAnalyzerManager.cs` |
| 6 | `Entities/Receipt.cs` |
| 6 | `Interfaces/ILogAnalyzerManager.cs` |

**Concentración del código:** `StationManager.cs` (506) + `StationController.cs` (227) = **733 líneas (21 % del proyecto)**. `Models/` + `DTOs/` + `Entities/` = 26 archivos (45 % de los archivos) con 400 líneas (11 % del código). Las 3 clases generadas por EF en `Migrations/` suman 430 líneas (12 %) sin código de negocio.

---

## 20. Estado de compilación y configuración del repositorio

### 20.1 Build
```
dotnet build  →  0 Errores, 115 Advertencias
```
Las advertencias son principalmente de nulabilidad (`CS8618` "Non-nullable field/property must contain a non-null value", `CS8601`, `CS8603`, `CS8629`), concentradas en:
- Asignaciones de `IConfiguration.GetConnectionString(...)` a campos no-nullable (`StationManager.cs:23,26`, `TransaccionesProcesadasManager.cs:20,26`)
- Propiedades `string` sin inicializar en DTOs/ViewModels (`LoginViewModel.cs:8,10`, `AuditDetailViewModel.cs:7,10,11`, `POVViewModel.cs:36,47,48,49`, `TransaccionesProcesadasJob.cs:93,94`, `TransaccionesProcesadasManager.cs:161`)
- Retornos nulos en métodos no-nullable (`ServiceManager.cs:77,81`)
- `MoneyFlow.Tests/CsvHelperTests.cs:23` (pasa `null` a un parámetro no-nullable)

### 20.2 Inventario de configuración del repositorio

| Elemento | Estado |
|---|---|
| `.gitignore` | ✅ Plantilla VisualStudio estándar. **Ignora `/MoneyFlow/appsettings.json`** (línea 364) |
| `.gitattributes` | ✅ Solo `* text=auto`. No fija `working-tree-encoding` |
| `LICENSE.txt` | ✅ |
| `README.md` | ⚠ 1 línea (`# MoneyFlow`) |
| `.editorconfig` | ❌ No existe |
| `Dockerfile` / `docker-compose.yml` | ❌ No existen |
| `global.json` | ❌ No existe (SDK sin pinear) |
| `Directory.Build.props` / `Directory.Packages.props` | ❌ No existen |
| `nuget.config` | ❌ No existe |
| `.github/workflows/` | ⚠ Directorio existe, **vacío** → sin CI/CD |
| `AGENTS.md` / `CLAUDE.md` / `.cursorrules` | ❌ No existen |
| `MoneyFlow.csproj.user` | ✅ Metadatos de scaffolder de Visual Studio |

### 20.3 Historial de Git relevante

| Commit | Mensaje | Efecto |
|---|---|---|
| `6321742` | `Revert "chore:Se agregaron cambios para trazabilidad y analisis de logs."` | HEAD actual. Revierte `78315f7`, dejando `ILogAnalyzerManager`, `LogAnalyzerManager` y `LogSearchCriteriaDTO` como restos |
| `78315f7` | `"chore:Se agregaron cambios para trazabilidad y analisis de logs."` | Módulo de análisis de logs (ya revertido) |
| `77e0c2c` | `"Features: Auditorias/Logs Analyzer…"` | Inicio del módulo de análisis de logs |
| `57b5d34` | `"Features: … reporte, listado y analisis para auditorias"` | Módulo de auditorías |
| `e02c9ae` | `"Features: Mods … Stations, Invnetarios, … logica de login"` | Estaciones, inventarios, login |
| `eb526e7` | `"Features: Modulos de Stations, Login y Logica para redireccionamiento…"` | Estaciones + login |
| `b516125` | `"… CRUD y ahora un nuevo modulo de busqueda por linked Server"` | Búsqueda por linked server |
| `57c8d89` | `"Add project files."` | Creación del proyecto: `AppDbContext`, entidades, migraciones |

**Estado del working tree (sin commitear):** modificaciones en `MoneyFlow.sln`, `AppDbContext.cs`, `StationController.cs`, `IStationManager.cs`, `StationManager.cs`, `MoneyFlow.csproj`, `Program.cs`, `_Layout.cshtml`, `Station/Index.cshtml`. Archivos nuevos sin trackear: todo el módulo `TransaccionesProcesadas` (controller, DTOs, entidad, manager, job, vistas), `Utilities/{CsvHelper,ExcelTransaccionesReader,HangfireAuthorizationFilter}.cs`, `MoneyFlow.Tests/` y `Scripts/`.

---

*Fin del documento. Analisis estático del repositorio en el commit `6321742`, incluyendo los cambios no commiteados del working tree (módulo TransaccionesProcesadas + Hangfire). Ningún dato fue extraído de una base de datos en ejecución; todas las tablas y columnas remotas se deducen de las consultas SQL presentes en el código.*