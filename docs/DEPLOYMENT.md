# Despliegue del piloto (coste 0, sin tarjeta)

Arquitectura del piloto tal como está desplegada:

```text
Navegador ──► Cloudflare Workers (static assets)     frontend React/Vite        gratis
     │
     └──────► Railway (Docker, plan Trial → Free)    API ASP.NET Core 8         sin tarjeta
                    │
                    ├──► TiDB Cloud Starter            base de datos compatible con MySQL  gratis
                    ├──► Friendly Captcha (v2)         captcha del formulario público      gratis (no comercial)
                    └──► EmailJS                       envío de correos                    gratis (200/mes)
```

## Por qué estos proveedores

Probado en noviembre de 2026, buscando planes gratuitos **sin tarjeta**:

| Necesidad | Elegido | Descartados y por qué |
| --- | --- | --- |
| API .NET en Docker | **Railway** | Render, Northflank y Koyeb piden tarjeta incluso en el plan gratuito. Hugging Face Spaces solo permite salida por los puertos 80/443 (TiDB usa el 4000). Back4App y Choreo no dejaban registrarse. |
| MySQL | **TiDB Cloud Starter** (5 GiB) | Aiven ya no tiene MySQL gratuito (solo PostgreSQL/Valkey). |
| Frontend | **Cloudflare Workers** | — |

Cloudflare Containers podría alojar también la API, pero requiere el plan Workers Paid (5 USD/mes).

**Railway:** la cuenta empieza con 30 días / 5 USD de prueba y luego pasa al plan Free con 1 USD de crédito al mes.
Con *Serverless* activado el contenedor se duerme sin tráfico y solo consume mientras está despierto; si un mes se agota
el crédito, el servicio se para hasta el mes siguiente. La primera petición tras dormir tarda hasta ~1 minuto (por eso el
frontend usa `VITE_API_TIMEOUT=60000`).

## Cambios de código que hacen posible este despliegue

| Cambio | Motivo |
| --- | --- |
| `Infrastructure/Migrations/` versionada | Sin migraciones, una base vacía nunca recibía el esquema. |
| Columnas GUID con `ascii_bin` en la migración inicial | TiDB rechaza `ascii_general_ci`, que Pomelo usa por defecto; `ascii_bin` vale en MySQL 8 y en TiDB. |
| `Database__AutoMigrate=true` | Aplica las migraciones al arrancar en cualquier entorno. |
| `MESSAGING_TRANSPORT=InMemory` | Sin RabbitMQ: los eventos se procesan en el mismo proceso (una sola instancia). |
| La API escucha en `$PORT` (por defecto 5137) | Railway inyecta el puerto. |
| `Cors__AllowedOrigins` | Añade el dominio de Cloudflare sin recompilar. |
| `GET /health` | Health check del host. |
| `X-Forwarded-*` + redirección HTTPS opcional (`Https__Redirect`) | La API va detrás de un proxy que ya termina TLS. |
| `register/admins` cerrado salvo `Setup__AllowAdminRegistration=true` | El endpoint era anónimo. |
| Cookie `refreshToken` con `SameSite=None; Secure` fuera de `Development` (`Auth__CrossSiteCookies`) | Frontend y API están en sitios distintos; con `Lax` la sesión no se renovaba. |
| `Frontend__BaseUrl` | El enlace del correo de activación apuntaba fijo a `http://localhost:5173`. |
| Formulario de reporte: `finishDate` vacío se envía como `null` | Con `""` la API rechazaba todo reporte sin fecha de fin del evento. |
| Formulario de reporte: errores reales en vez de "captcha inválido" | Cualquier 400 se mostraba como fallo de captcha. |
| Captcha verificado en el servidor (reporte público, consulta y login) | Estaba comentado: el widget se mostraba pero cualquier bot podía enviar reportes o probar contraseñas. |

## 1. Base de datos: TiDB Cloud Starter

1. tidbcloud.com → **Create Resource → Starter**, compatibilidad **MySQL**, límite de gasto **0 USD**.
2. **Connect** → *Public Endpoint* → *Generate Password* (solo se muestra una vez).
3. En el SQL Editor:
   ```sql
   SET GLOBAL default_collation_for_utf8mb4 = 'utf8mb4_0900_ai_ci';
   CREATE DATABASE finlay CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
   ```
   Sin el `SET GLOBAL`, TiDB usa `utf8mb4_bin` (distingue mayúsculas) en vez del comportamiento de MySQL 8.
4. Cadena de conexión:
   ```text
   server=gateway01.<region>.prod.aws.tidbcloud.com;port=4000;database=finlay;uid=<usuario>.root;pwd=<clave>;SslMode=VerifyFull;
   ```
   La regla de red por defecto (`Allow_all_public_connections`) es necesaria: Railway no tiene IP fija.

## 2. Friendly Captcha y EmailJS

- **Friendly Captcha:** crear una aplicación **v2**, endpoint *Global*. Añadir el dominio de Cloudflare (y `localhost`) en
  *Domain List* y activar *Only allow requests from the Domain List*. Crear una API key con permiso *Captcha Siteverify*.
- **EmailJS:** servicio de correo (p. ej. Gmail) y **una sola plantilla** con *To Email* `{{email}}` y contenido
  `{{{body}}}` (tres llaves: el backend envía el HTML ya armado). Activar *Do not save private data* (los correos llevan
  datos de salud) y, en *Account → Security*, **Allow EmailJS API for non-browser applications**.

## 3. API en Railway

1. railway.com → *New project → GitHub Repository* → el repositorio. Railway lee `render.yaml` e importa varias variables,
   la carpeta `backend` y el health check.
2. *Settings:* Root Directory `backend`, Healthcheck `/health`, **Serverless** activado, Watch Paths `/backend/**`.
3. *Variables:*

   | Variable | Valor |
   | --- | --- |
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `MESSAGING_TRANSPORT` | `InMemory` |
   | `Database__AutoMigrate` | `true` |
   | `Setup__AllowAdminRegistration` | `false` |
   | `ConnectionStrings__AppDbConnectionString` | cadena del paso 1 (secreto) |
   | `JwtSettings__Secret` | aleatorio, p. ej. `openssl rand -base64 48` (secreto) |
   | `Encryption__AesKey`, `Encryption__BlindIndexKey` | dos `openssl rand -base64 32` distintos (secreto, **no cambiar nunca**) |
   | `FriendlyCaptcha__SiteKey` / `FriendlyCaptcha__ApiKey` | site key (pública) / API key (secreto) |
   | `Email__EmailJS__ServiceId`, `UserId`, `HtmlTemplateId` | IDs de EmailJS (UserId = public key) |
   | `Email__EmailJS__AccessToken` | private key de EmailJS (secreto) |
   | `Cors__AllowedOrigins` | URL del frontend, sin barra final |
   | `Frontend__BaseUrl` | URL del frontend (enlaces de los correos) |
   | `DOTNET_gcServer` | `0` (menos RAM, menos crédito consumido) |

4. Comprobar `https://<servicio>.up.railway.app/health` → `{"status":"healthy"}`.

## 4. Frontend en Cloudflare

Workers & Pages → *Create → Import a repository*: nombre `finlay-pharmacovigilance` (igual que `wrangler.jsonc`), Root
directory `frontend`, build `npm ci && npm run build`, deploy `npx wrangler deploy`. `frontend/.env.production` ya trae la
URL de la API y la site key del captcha, así que no hacen falta variables de build.

## 5. Puesta en marcha

1. **Primer administrador:** `Setup__AllowAdminRegistration=true`, desplegar, y:
   ```bash
   curl -X POST https://<api>/api/Authentication/register/admins -H "Content-Type: application/json" \
     -d '{"UserName":"admin","Email":"...","Password":"...","PhoneNumber":"..."}'
   ```
   Volver a `false` y desplegar. Con datos válidos, el endpoint debe responder 404.
2. **Catálogos:** síntomas, vacunas y lotes desde *Gestionar Catálogo*. Los **centros de vacunación no tienen pantalla**: se
   crean con `POST /api/VaccinationCenter/register` (rol Admin).
3. **Jefes de sección:** cada reporte se asigna al jefe de sección del **municipio del paciente**. Sin uno en ese municipio,
   el reporte se rechaza con *No SectionResponsible found*.

El cliente `finlay_client/` puede cargar datos de ejemplo, pero no lee argumentos: toma la URL de `finlay_client/.env`
(`API_BASE_URL=https://<api>`, sin `/api`), ejecuta solo los seeds no comentados en `Program.cs` e inicia sesión con
administradores de prueba con contraseña fija en el código. No usarlo contra un despliegue público sin borrar esos usuarios.

## 6. Limitaciones conocidas del piloto

- **Captcha:** el servidor verifica el token en el reporte público, la consulta de reporte y el login. Los administradores y
  médicos revisores autenticados envían reportes sin captcha. Si Friendly Captcha no responde, nadie puede iniciar sesión.
- **Correos desactivados en el código:** la confirmación del reporte, la asignación al revisor y el aviso al reportante están
  comentados en sus consumers. Solo se envía el correo de activación de los médicos revisores. Crear un jefe de sección no
  envía correo.
- **Datos personales en los logs:** `EmailJsService` escribe en consola el HTML completo de cada correo.
- **Mensajería en memoria:** si el servicio se reinicia, los eventos pendientes se pierden. Una sola instancia.
- **Cookies de terceros:** Safari (y navegadores que bloquean cookies de terceros) no envía la cookie de renovación; allí la
  sesión caduca con el access token (60 min). Se resuelve con un dominio propio común para frontend y API.
- **Logs** (`logs/`) en disco efímero: usar los de Railway.
- **Límites de peticiones** (`RateLimitingMiddleware`) prácticamente ilimitados: ajustarlos antes de abrir el sistema.
- **WhatsApp** (`WhatsApp__*`) opcional; no configurado.
- Los planes gratuitos cambian: revisar las condiciones de cada proveedor.
