# JamBurgers 🍔 — Sistema de Delivery

Sistema completo de delivery de hamburguesas desarrollado como proyecto académico para la materia **Programación III (Desarrollo Móvil)** del ITES. Incluye una API REST como backend y una aplicación móvil/de escritorio como frontend.

---

## Índice

- [Stack técnico](#stack-técnico)
- [Arquitectura general](#arquitectura-general)
- [Roles y permisos](#roles-y-permisos)
- [Flujo de pedidos](#flujo-de-pedidos)
- [Seguridad](#seguridad)
- [Manejo de imágenes](#manejo-de-imágenes)
- [Pagos con MercadoPago](#pagos-con-mercadopago)
- [Comunicación en tiempo real (SignalR)](#comunicación-en-tiempo-real-signalr)
- [Notificaciones locales (Android)](#notificaciones-locales-android)
- [Correo electrónico](#correo-electrónico)
- [Pantallas por rol](#pantallas-por-rol)
- [Modelo de datos](#modelo-de-datos)
- [Instalación y configuración](#instalación-y-configuración)
- [Variables de entorno](#variables-de-entorno)

---

## Stack técnico

| Capa | Tecnología |
|---|---|
| **Backend** | .NET 9 — ASP.NET Core Web API |
| **Frontend** | .NET 9 — MAUI Blazor Hybrid (Android + Windows) |
| **Base de datos** | SQL Server Express (`DeliveryDB`) |
| **ORM** | Entity Framework Core |
| **Hashing** | BCrypt.Net-Next |
| **Tiempo real** | SignalR (`DeliveryHub`) |
| **Pagos** | MercadoPago Checkout Pro (SDK oficial) |
| **Email** | SMTP vía `IEmailSender` |
| **Notificaciones** | Plugin.LocalNotification 11.1.2 |

### Repositorios

- **Backend:** `jexzus/WebApiDelivery`
- **Frontend:** `jexzus/MBH-Delivery`

---

## Arquitectura general

```
┌─────────────────────────────────┐
│   App MAUI Blazor (Android/Win) │
│   - Razor Components            │
│   - Services (HTTP + SignalR)   │
└────────────┬────────────────────┘
             │ HTTP / SignalR (WebSocket)
┌────────────▼────────────────────┐
│   ASP.NET Core Web API          │
│   - Controllers                 │
│   - DeliveryHub (SignalR)       │
│   - EF Core                     │
└────────────┬────────────────────┘
             │
┌────────────▼────────────────────┐
│   SQL Server Express            │
│   Base de datos: DeliveryDB     │
└─────────────────────────────────┘
```

**URLs por plataforma (configuradas en `MauiProgram.cs`):**
- Android: `http://192.168.0.13:5224/`
- Windows / Debug: `https://localhost:7189/`

---

## Roles y permisos

El sistema tiene **5 roles** diferenciados con accesos y capacidades distintas.

### 1. `superadmin`

El rol de mayor jerarquía. Tiene todos los permisos de admin más la capacidad de gestionar a los propios administradores.

| Acción | ✅/❌ |
|---|---|
| Ver y cambiar estado de pedidos | ✅ |
| Gestionar catálogo de productos | ✅ |
| Crear / editar / eliminar administradores | ✅ |
| Gestionar vendedores | ✅ |
| Gestionar repartidores | ✅ |
| Solicitar repartidor para un pedido | ✅ |
| Cancelar cualquier pedido | ✅ |

### 2. `admin`

Mismos permisos que `superadmin` excepto la gestión de otros administradores.

| Acción | ✅/❌ |
|---|---|
| Ver y cambiar estado de pedidos | ✅ |
| Gestionar catálogo de productos | ✅ |
| Crear / editar / eliminar administradores | ❌ |
| Gestionar vendedores | ✅ |
| Gestionar repartidores | ✅ |
| Solicitar repartidor para un pedido | ✅ |
| Cancelar cualquier pedido | ✅ |

### 3. `vendedor`

Rol operativo de sala/mostrador. Ve y gestiona pedidos pero no administra el sistema.

| Acción | ✅/❌ |
|---|---|
| Ver pedidos y cambiar estado | ✅ |
| Solicitar repartidor | ✅ |
| Cancelar pedidos | ✅ |
| Gestionar catálogo | ❌ |
| Gestionar administradores / vendedores / repartidores | ❌ |

### 4. `repartidor`

Rol exclusivo para el reparto a domicilio. Solo ve los pedidos que le son asignados.

| Acción | ✅/❌ |
|---|---|
| Ver pedidos disponibles para tomar | ✅ |
| Aceptar un pedido (ventana de 5 minutos) | ✅ |
| Marcar pedido como entregado | ✅ |
| Cancelar reparto (dentro de los 5 minutos) | ✅ |
| Ver pedidos de otros repartidores | ❌ |
| Gestionar catálogo o usuarios | ❌ |

> **Ventana de 5 minutos:** una vez que el repartidor acepta un pedido, tiene 5 minutos para cancelar el reparto si no puede realizarlo. Pasado ese tiempo, la cancelación queda bloqueada.

### 5. `cliente`

Usuario final que realiza pedidos.

| Acción | ✅/❌ |
|---|---|
| Ver catálogo de productos | ✅ |
| Agregar / quitar productos del carrito | ✅ |
| Confirmar pedido (domicilio o retiro) | ✅ |
| Elegir forma de pago (efectivo / MercadoPago) | ✅ |
| Ver estado de sus pedidos en tiempo real | ✅ |
| Cancelar pedido (solo desde estado Pendiente) | ✅ |
| Ver pedidos de otros clientes | ❌ |
| Acceder a paneles de gestión | ❌ |

---

## Flujo de pedidos

### Estados posibles

```
Pendiente → En preparación → EsperandoRepartidor → En reparto → Entregado
                          ↘ Para retirar ↗
                                     ↘ Entregado
           (en cualquier estado)
                          ↘ Cancelado
```

### Pedido a domicilio

1. **Pendiente** — Pedido recibido, esperando acción del admin/vendedor.
2. **En preparación** — Cocina está preparando el pedido.
3. **EsperandoRepartidor** — Admin solicita repartidor; se emite alerta a todos los repartidores disponibles vía SignalR.
4. **En reparto** — Un repartidor aceptó el pedido y está en camino.
5. **Entregado** — Pedido entregado. El repartidor vuelve a estar disponible.

### Pedido de retiro en tienda

1. **Pendiente** → **En preparación** → **Para retirar** → **Entregado**

### Reglas de negocio

- Un pedido **a domicilio** no puede pasar a "Para retirar" y viceversa.
- El cliente solo puede **cancelar** desde el estado **Pendiente**.
- Admin y vendedor pueden cancelar desde **cualquier estado**.
- Si el pago es por **MercadoPago**, el pedido **no puede avanzar** (de Pendiente en adelante) hasta que el pago sea confirmado por MercadoPago. La cancelación siempre está permitida.

---

## Seguridad

### Hashing de contraseñas

Todas las contraseñas (clientes, admins, vendedores, repartidores) se almacenan usando **BCrypt** con `workFactor: 10`. Nunca se guarda la contraseña en texto plano.

```csharp
// Al guardar
BCrypt.Net.BCrypt.HashPassword(contraseña, workFactor: 10)

// Al verificar login
BCrypt.Net.BCrypt.Verify(contraseñaIngresada, hashGuardado)
```

### Pre-registro de clientes (verificación por email)

El registro de nuevos clientes es un proceso de **dos pasos** para verificar que el email sea real:

1. **Paso 1 — Solicitar código:**
   - El usuario ingresa su email.
   - El sistema genera un **código numérico aleatorio de 6 dígitos**.
   - El código se hashea con BCrypt (`workFactor: 10`) y se guarda en la tabla `PreRegistroToken` junto con la fecha de expiración.
   - Se envía el código en texto plano al email del usuario.
   - **Expiración: 10 minutos.**

2. **Paso 2 — Verificar código y completar registro:**
   - El usuario ingresa el código recibido más sus datos personales.
   - El sistema verifica: existencia del registro, no expirado, no ya usado, y que el código coincida con el hash guardado (`BCrypt.Verify`).
   - Si todo es válido, se marca el token como `Usado = true` y se crea la cuenta.

> **El código es de uso único.** Una vez verificado, el token queda marcado y no puede reutilizarse aunque no haya expirado.

### Recuperación de contraseña

Proceso idéntico al pre-registro en cuanto al manejo del token:

1. El usuario ingresa su email registrado.
2. Se genera y hashea un código de 6 dígitos → se envía por email.
3. **Expiración: 10 minutos, uso único.**
4. El usuario ingresa el código + nueva contraseña.
5. El sistema verifica el token y actualiza la contraseña con BCrypt.

### Validaciones de campos en registro

| Campo | Restricción |
|---|---|
| Nombre de usuario | Único en el sistema |
| Contraseña | Alfanumérica |
| Nombre / Apellido | Solo letras |
| Teléfono | Solo numérico |
| Domicilio | Texto libre |

### Seguridad en imágenes

Las imágenes se sirven desde el servidor y se convierten a **base64 Data URL** en el cliente, evitando el problema de Mixed Content en Android (que bloquea peticiones HTTP desde contextos HTTPS).

---

## Manejo de imágenes

### Almacenamiento

Las imágenes de productos se guardan en el servidor dentro de la carpeta `wwwroot/imagenes/`. Se almacena únicamente el **nombre del archivo** en la base de datos (ej: `hamburguesa-clasica.jpg`).

### Carga en el frontend — `ImagenCacheService`

El frontend no consume directamente la URL de la imagen (lo que causaría errores de Mixed Content en Android al mezclar HTTP y HTTPS). En cambio:

1. El servicio descarga los bytes de la imagen vía HTTP desde el servidor.
2. Convierte los bytes a una cadena **base64 Data URL** (`data:image/jpeg;base64,...`).
3. Almacena el resultado en un **diccionario en memoria** (caché) indexado por nombre de archivo.
4. Las subsiguientes solicitudes de la misma imagen se sirven desde la caché sin hacer una nueva petición HTTP.

```csharp
var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
```

Esto garantiza que las imágenes se vean correctamente tanto en Android como en Windows, sin importar el protocolo.

---

## Pagos con MercadoPago

### Integración

Se usa **MercadoPago Checkout Pro** en modalidad sandbox para pruebas. La integración usa el SDK oficial de MercadoPago para .NET.

### Flujo de pago

```
Cliente elige "MercadoPago"
        ↓
POST /api/pagos/crear-preferencia
  → Crea preferencia en MP con items del pedido
  → Guarda MpPreferenceId en BD
  → Devuelve sandboxInitPoint (URL de pago)
        ↓
App abre URL en navegador (checkout MP)
        ↓
Cliente paga en MP
        ↓
MP llama POST /api/pagos/webhook
  → Consulta detalles del pago a MP API
  → Actualiza EstadoPago en BD: "aprobado" / "rechazado"
  → Dispara evento SignalR "PagoActualizado" al grupo Admins
        ↓
MP redirige a GET /api/pagos/retorno
  → Muestra página HTML con resultado
        ↓
Cliente vuelve a la app y ve el estado actualizado
```

### Estados de pago (`EstadoPago` en tabla `Pedido`)

| Valor | Significado |
|---|---|
| `pendiente` | Pago no completado aún (estado inicial) |
| `aprobado` | Pago confirmado por MercadoPago vía webhook |
| `rechazado` | Pago rechazado por MercadoPago |

### Restricción de avance de pedido

Si la forma de pago es MercadoPago y el `EstadoPago` no es `"aprobado"`, el backend rechaza cualquier cambio de estado que no sea "Cancelado". Esto previene que se entregue un pedido sin pago confirmado.

### Configuración necesaria (producción)

En el panel de MercadoPago se debe configurar la URL del webhook apuntando a:
```
https://tu-dominio/api/pagos/webhook
```

---

## Comunicación en tiempo real (SignalR)

### Hub: `DeliveryHub`

El hub central de comunicación en tiempo real. Cuando un cliente se conecta, se une automáticamente al grupo correspondiente a su rol.

### Grupos

| Grupo | Quiénes están |
|---|---|
| `Admins` | Admins y vendedores |
| `Repartidores` | Todos los repartidores conectados |
| `Cliente_{idCliente}` | Un cliente específico |

### Eventos del servidor al cliente

| Evento | Destinatario | Datos |
|---|---|---|
| `EstadoCambiadoGlobal` | Todos | numPedido, nuevoEstado, estadoAnterior, modoEntrega, idCliente |
| `MiPedidoActualizado` | Cliente específico | numPedido, nuevoEstado, modoEntrega |
| `NuevoPedidoParaReparto` | Repartidores | datos del pedido disponible |
| `PedidoAceptado` | Admins | numPedido, nombreRepartidor, repartidorId |
| `PedidoAsignado` | Repartidores | numPedido, repartidorId |
| `PedidoCancelado` | Repartidores | numPedido |
| `CerrarAlerta` | Repartidores | numPedido |
| `PedidoRevertido` | Admins | numPedido |
| `AlertaAdminVista` | Admins | numPedido |
| `AlertaPedidoIgnorada` | Admins | numPedido, mensaje, tipo |
| `PagoActualizado` | Admins | numPedido, estadoPago |

### Estructuras internas del hub

El hub mantiene dos `ConcurrentDictionary` en memoria:
- **Conexiones activas:** mapea `connectionId → (idUsuario, rol)`
- **Pedidos en búsqueda:** mapea `numPedido → timestamp` para controlar la ventana de 5 minutos del repartidor

---

## Notificaciones locales (Android)

Usando el paquete `Plugin.LocalNotification 11.1.2`, la app muestra notificaciones en el sistema operativo Android cuando:

- Llega un nuevo pedido (admins/vendedores)
- Un repartidor acepta un pedido
- Un pedido cambia de estado (cliente)

Las notificaciones funcionan con la **pantalla bloqueada** y al hacer tap navegan directamente a la página correspondiente dentro de la app.

---

## Correo electrónico

El sistema envía emails automáticos en dos situaciones:

| Situación | Destinatario | Contenido |
|---|---|---|
| Pre-registro | Nuevo cliente | Código de verificación de 6 dígitos (válido 10 min) |
| Recuperación de contraseña | Cliente registrado | Código de recuperación de 6 dígitos (válido 10 min) |

La implementación usa `IEmailSender` con transporte SMTP configurable vía `appsettings.json`.

---

## Pantallas por rol

### Admin / Superadmin / Vendedor

| Ruta | Descripción |
|---|---|
| `/paneladmin` | Panel principal con accesos directos |
| `/panelvendedor` | Panel principal (versión vendedor) |
| `/gestionpedidos` | Lista de pedidos con filtros, búsqueda, detalle e impresión |
| `/gestioncatalogo` | ABM de productos con imagen |
| `/nuevoproducto` | Formulario de nuevo producto |
| `/editarproducto` | Formulario de edición de producto |
| `/gestionadministradores` | ABM de administradores (solo superadmin) |
| `/gestionvendedores` | ABM de vendedores |
| `/gestionrepartidores` | ABM de repartidores |

### Repartidor

| Ruta | Descripción |
|---|---|
| `/panelrepartidor` | Vista de pedidos disponibles y en curso |

### Cliente

| Ruta | Descripción |
|---|---|
| `/catalogo` | Catálogo de productos con búsqueda |
| `/carrito` | Carrito de compras y confirmación de pedido |
| `/estadopedidos` | Historial de pedidos con línea de tiempo visual |

### Autenticación (sin sesión)

| Ruta | Descripción |
|---|---|
| `/login` | Inicio de sesión (detecta rol y redirige) |
| `/registro` | Pre-registro con verificación de email |
| `/recuperar-contrasena` | Recuperación de contraseña |

---

## Modelo de datos

### Tabla `Usuario`
| Campo | Tipo | Descripción |
|---|---|---|
| `IdUsuario` | int PK | Identificador |
| `NombreUsuario` | string | Único en el sistema |
| `Contraseña` | string | Hash BCrypt |
| `Rol` | string | `superadmin`, `admin`, `vendedor`, `repartidor`, `cliente` |

### Tabla `Cliente`
| Campo | Tipo | Descripción |
|---|---|---|
| `IdCliente` | int PK | Identificador |
| `IdUsuario` | int FK | Referencia a `Usuario` |
| `Nombre` | string | |
| `Apellido` | string | |
| `NumTelefono` | string | |
| `Domicilio` | string | |

### Tabla `Producto`
| Campo | Tipo | Descripción |
|---|---|---|
| `IdProducto` | int PK | Identificador |
| `NombreProducto` | string | |
| `Descripcion` | string? | |
| `Precio` | decimal(18,2) | |
| `Imagen` | string? | Nombre de archivo en `wwwroot/imagenes/` |

### Tabla `Pedido`
| Campo | Tipo | Descripción |
|---|---|---|
| `NumPedido` | int PK | Identificador |
| `IdCliente` | int FK | Cliente que realizó el pedido |
| `IdRepartidor` | int? FK | Repartidor asignado |
| `FechaPedido` | DateTime | |
| `EstadoPedido` | string | Pendiente / En preparación / EsperandoRepartidor / En reparto / Para retirar / Entregado / Cancelado |
| `MontoTotal` | decimal(18,2) | |
| `Observaciones` | string? | Notas del cliente |
| `ModoEntrega` | string? | `Domicilio` o `Tienda` |
| `FormaPago` | string? | `efectivo` o `mercadopago` |
| `EstadoPago` | string | `pendiente` / `aprobado` / `rechazado` |
| `MpPreferenceId` | string? | ID de preferencia de MercadoPago |
| `MpPaymentId` | string? | ID del pago confirmado por MP |
| `MpStatusDetail` | string? | Detalle del estado de MP |

### Tabla `DetallePedido`
| Campo | Tipo | Descripción |
|---|---|---|
| `IdDetalle` | int PK | |
| `NumPedido` | int FK | |
| `IdProducto` | int FK | |
| `Cantidad` | int | |
| `PrecioUnitario` | decimal(18,2) | Precio al momento del pedido |
| `Subtotal` | decimal (calculado) | Cantidad × PrecioUnitario |

### Tabla `PreRegistroToken`
| Campo | Tipo | Descripción |
|---|---|---|
| `Id` | int PK | |
| `Email` | string | |
| `TokenHash` | string | Hash BCrypt del código de 6 dígitos |
| `ExpiresAt` | DateTime | Expiración (10 minutos desde creación) |
| `Usado` | bool | Marca de uso único |

---

## Instalación y configuración

### Requisitos

- .NET 9 SDK
- SQL Server Express (o SQL Server)
- Visual Studio 2022+ con workload MAUI
- Android SDK (para compilar para Android)

### Backend

```bash
git clone https://github.com/jexzus/WebApiDelivery
cd WebApiDelivery
dotnet restore
```

1. Configurar `appsettings.json` (ver sección siguiente)
2. Aplicar migraciones:
   ```bash
   dotnet ef database update
   ```
3. Ejecutar:
   ```bash
   dotnet run
   ```

### Frontend

```bash
git clone https://github.com/jexzus/MBH-Delivery
cd MauiBlazorDelivery
dotnet restore
```

1. En `MauiProgram.cs`, actualizar la URL base del backend según plataforma.
2. Ejecutar desde Visual Studio seleccionando target (Android / Windows).

---

## Variables de entorno

Configurar en `appsettings.json` (o variables de entorno en producción):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=DeliveryDB;..."
  },
  "MercadoPago": {
    "AccessToken": "APP_USR-...",
    "BaseUrl": "https://tu-dominio"
  },
  "Email": {
    "Host": "smtp.proveedor.com",
    "Port": 587,
    "Usuario": "tu@email.com",
    "Contraseña": "..."
  }
}
```

> En producción nunca commitear credenciales reales. Usar variables de entorno o un gestor de secretos.

---

## Impresión

La app incluye soporte de impresión para admins/vendedores usando un helper JavaScript (`mauiPrintHelper`):

- **Lista de pedidos:** imprime el elemento con clase `print-list`
- **Detalle de pedido:** imprime el elemento con clase `print-detalle`

El CSS incluye reglas `@media print` que ocultan todo el resto de la interfaz durante la impresión.

---

## Licencia

Proyecto académico — ITES, Programación III Móvil.
