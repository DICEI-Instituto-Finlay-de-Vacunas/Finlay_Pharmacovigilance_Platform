# Despliegue del piloto (coste 0)

Arquitectura del piloto:

```text
Navegador ──► Cloudflare (Workers + static assets)   frontend React/Vite, gratis
     │
     └──────► Render (Docker, plan free)             API ASP.NET Core 8, gratis
                    │
                    └──► MySQL 8 gestionado (plan free)
```

## Por qué el backend no corre en Cloudflare

La API es .NET 8 con MySQL. Los Workers gratuitos no ejecutan .NET y **Cloudflare Containers requiere el plan
Workers Paid (5 USD/mes)**. Para mantener coste 0, el frontend va en Cloudflare y la API en un host Docker gratuito
(Render). Si más adelante se acepta pagar 5 USD/mes, la API puede moverse a Cloudflare Containers sin cambios de código.

## Cambios de código que hacen posible este despliegue

| Cambio | Motivo |
| --- | --- |
| `Infrastructure/Migrations/` ahora versionada (antes estaba en `.gitignore`) | Sin migraciones, una base de datos vacía nunca recibía el esquema. |
| `Database__AutoMigrate=true` | Aplica las migraciones al arrancar en cualquier entorno (antes solo en `Development`). |
| `MESSAGING_TRANSPORT=InMemory` | RabbitMQ no tiene plan gratuito autoalojable aquí; los eventos (emails/alertas) se procesan en el mismo proceso. |
| La API escucha en `$PORT` (por defecto 5137) | Los hosts inyectan el puerto; antes estaba fijo en 5137. |
| `Cors__AllowedOrigins` | Permite añadir el dominio de Cloudflare sin recompilar. |
| `GET /health` | Health check para el host. |
| `X-Forwarded-*` + redirección HTTPS opcional (`Https__Redirect`) | La API va detrás de un proxy que ya termina TLS. |
| `POST /api/Authentication/register/admins` cerrado fuera de `Development` salvo `Setup__AllowAdminRegistration=true` | El endpoint era anónimo: cualquiera podía crearse un administrador en un despliegue público. |

Probado en local: build, arranque en `Production`, migración automática sobre MySQL 8 (29 tablas), roles, alta de admin,
login, CORS (origen configurado permitido y otros rechazados), cierre del registro de admins, y el frontend sobre el runtime
de Workers (`wrangler dev`) con fallback de SPA.

## 1. Base de datos MySQL gratuita

Cualquier MySQL 8 accesible desde Internet con TLS sirve. Opciones con plan gratuito: Aiven for MySQL (free) o
TiDB Cloud Serverless (compatible con MySQL). Cadena de conexión (Pomelo):

```text
server=HOST;port=PUERTO;database=finlay;uid=USUARIO;pwd=CLAVE;SslMode=Required;
```

> Solo se ha validado contra MySQL 8 local. Si se usa TiDB y la migración inicial falla, avisar para ajustarla.

## 2. API en Render

1. Render → **New → Blueprint** → elegir este repositorio (usa `render.yaml`).
2. Completar los valores marcados como secretos:
   - `ConnectionStrings__AppDbConnectionString`: la cadena del paso 1.
   - `Encryption__AesKey` y `Encryption__BlindIndexKey`: dos valores distintos de `openssl rand -base64 32`.
     **No cambiarlos después**: los datos personales cifrados dejarían de poder leerse.
   - `FriendlyCaptcha__ApiKey` y `FriendlyCaptcha__SiteKey`: cuenta gratuita de Friendly Captcha.
   - `Email__EmailJS__*`: credenciales de EmailJS (notificaciones por correo).
   - `Cors__AllowedOrigins`: URL del frontend, p. ej. `https://finlay-pharmacovigilance.<subdominio>.workers.dev`.
3. Comprobar `https://<servicio>.onrender.com/health` → `{"status":"healthy"}`.
4. **Crear el primer administrador** (una sola vez):
   - Poner `Setup__AllowAdminRegistration=true` y redesplegar.
   - `POST https://<servicio>.onrender.com/api/Authentication/register/admins` con
     `{"UserName":"...","Email":"...","Password":"...","PhoneNumber":"..."}`.
   - Volver a poner `Setup__AllowAdminRegistration=false` y redesplegar.
   - El resto de usuarios se crean desde la aplicación con ese administrador.

El servicio gratuito de Render se duerme tras ~15 min sin tráfico; la primera petición tarda hasta ~1 minuto (por eso el
frontend usa `VITE_API_TIMEOUT=60000`).

## 3. Frontend en Cloudflare

Variables de build (`frontend/.env.production` trae valores por defecto):

| Variable | Valor |
| --- | --- |
| `VITE_API_BASE_URL` | `https://<servicio>.onrender.com/api` (debe terminar en `/api`) |
| `VITE_FRIENDLYCAPTCHA_SITE_KEY` | Site key pública de Friendly Captcha |

### Opción A — Integración Git del dashboard (la más simple)

Cloudflare → **Workers & Pages → Create → Import a repository** → este repositorio, y configurar:

- Root directory: `frontend`
- Build command: `npm ci && npm run build`
- Deploy command: `npx wrangler deploy`
- Variables de build: `VITE_API_BASE_URL`, `VITE_FRIENDLYCAPTCHA_SITE_KEY`

Cada push a `main` redespliega. El plan Free incluye peticiones de assets estáticos ilimitadas.

### Opción B — GitHub Actions

Crear en GitHub los secretos `CLOUDFLARE_API_TOKEN` (permiso *Workers Scripts: Edit*) y `CLOUDFLARE_ACCOUNT_ID`, y
opcionalmente las variables `VITE_API_BASE_URL` / `VITE_FRIENDLYCAPTCHA_SITE_KEY`. El workflow
`.github/workflows/deploy-frontend.yml` despliega en cada push a `main` que toque `frontend/` (si faltan los secretos, se omite).

### Opción C — Manual

```bash
cd frontend
npm ci
CLOUDFLARE_API_TOKEN=... CLOUDFLARE_ACCOUNT_ID=... npm run deploy
```

## 4. Limitaciones conocidas del piloto

- **Mensajería en memoria**: si Render reinicia el servicio, los eventos pendientes (correos/alertas) se pierden. Aceptable
  para un piloto; para producción, volver a RabbitMQ (`MESSAGING_TRANSPORT` sin definir y `RABBITMQ_URL`).
- **Una sola instancia**: la mensajería en memoria no es compartida entre instancias.
- **Archivos de log** (`logs/`) viven en disco efímero; usar los logs de Render.
- **Límites de peticiones** (`RateLimitingMiddleware`) están configurados con valores prácticamente ilimitados; conviene
  ajustarlos antes de exponer el sistema más allá del piloto.
- **WhatsApp** (`WhatsApp__*`) es opcional y no está en el blueprint; configurarlo solo si se usa.
- Los planes gratuitos pueden cambiar o caducar (la BD gratuita en particular): revisar las condiciones del proveedor elegido.
