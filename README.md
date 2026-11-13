# Backend .NET 10 – creación de tickets Jira

Puerta de entrada a Jira Software para el agente de **Pase** (hackatón IntelliHack). El backend del agente
(`jurektumba/hackatonbcpnov26backend`, `tools/jira`) la llama por HTTP.

Variables de entorno (token en https://id.atlassian.com/manage-profile/security/api-tokens):

```
Jira__Email=correo-de-la-cuenta-del-token
Jira__ApiToken=xxxx
Security__ApiKey=clave-compartida   # recomendado: exige el header X-Api-Key en /api/* (salvo /api/health)
# opcionales: Jira__BaseUrl, Jira__ProjectKey (GRUP), Jira__IssueType (Tarea),
#             Jira__SubtaskIssueType (Subtask), Jira__SprintFieldId (customfield_10020)
```

Ejecutar: `dotnet run --urls http://localhost:8080`

## Despliegue en Azure

```bash
# En Azure Cloud Shell (Bash), sube jira-backend-azure.zip:
mkdir -p ~/jira-backend-azure
unzip -o ~/jira-backend-azure.zip -d ~/jira-backend-azure
cd ~/jira-backend-azure
bash deploy.sh
```

Crea un plan gratuito F1 Linux y App Service con .NET 10. Despliega `app.zip` ya publicado, configura Jira y comprueba que `/api/health` devuelve `{"status":"ok"}`. No usa `az webapp up` ni compila en Azure.

El paquete no contiene credenciales. El script pide el token sin mostrarlo; tambien acepta `JIRA_API_TOKEN` o un archivo `token` junto al script. No ejecutes con `bash -x`. Azure Cloud Shell ya tiene Azure CLI y la sesion iniciada.

Suscripcion predeterminada: **Suscripcion Dev**, `d9cbd201-81d6-4884-840f-3f29b7fd7ad7`. El script la selecciona explicitamente y muestra el nombre y el ID antes de crear recursos. Se puede cambiar con `SUBSCRIPTION_ID`.

Variables opcionales: `APP_NAME`, `PLAN_NAME`, `LOCATION`, `RESOURCE_GROUP`, `CORS_ORIGIN` (URL del frontend), `JIRA_EMAIL` (si falta, el script lo pide), `SECURITY_API_KEY` (si falta, el script genera una y la muestra al final). Los nombres predeterminados son estables para reutilizar recursos en posteriores despliegues. Un grupo puede contener recursos de distintas regiones.

### Alternativa B1 de pago autorizada

Ejecuta `bash deploy-b1.sh` desde el paquete extraido. Selecciona Suscripcion Dev, Central US y un plan B1 Linux de una instancia, con nombres fijos para reutilizarlo. No actualiza un plan F1 existente a pago: utiliza `plan-jira-b1-centralus`.

Precio publico consultado: USD 0.018/hora, aproximadamente USD 13.14 por 730 horas, sin impuestos ni posibles extras. La facturacion depende del contrato. Detener la app no detiene el cargo del plan. Si falla el despliegue despues de crear el plan, este puede seguir generando cargos; revisa los recursos en Azure. Antes de eliminarlo, verifica que no aloje otras aplicaciones.

La API de uso/cuotas puede mostrar limites distintos a los aplicados durante la creacion. Un limite publicado de 30 no garantiza capacidad ni aprobacion; si Azure rechaza B1, el script termina sin probar otros SKU.

Si Azure rechaza F1 por cuota o politica, el script termina con el error: no cambia automaticamente a un plan de pago. Puedes solicitar cuota o probar una region permitida con `LOCATION=centralus bash deploy.sh`; no se garantiza disponibilidad por suscripcion. F1 tiene limites de CPU y arranque en frio.

Para regenerar `app.zip` tras cambios, ejecuta desde esta carpeta `dotnet publish -c Release -o bin/azure-publish --self-contained false` y comprime el **contenido** de esa salida (sin carpeta contenedora).

La API se protege con `X-Api-Key` (`Security__ApiKey`). Sin esa variable acepta llamadas sin autenticacion: CORS no impide llamadas externas para crear tickets.

## Endpoints

Todas las rutas salvo `/api/health` exigen `X-Api-Key` si `Security__ApiKey` está definido. Errores de Jira: `502`
con `jiraStatus` y `detail`.

| Método | Ruta | Para qué |
|---|---|---|
| `GET` | `/api/health` | Verificar que la API está arriba |
| `GET` | `/api/projects` | Proyectos donde la cuenta puede crear issues → `[{ "key", "name" }]` |
| `GET` | `/api/projects/{key}/active-sprint` | Sprint activo del proyecto → `{ "id", "name", "state" }` o `null` (backlog) |
| `POST` | `/api/tickets` | Crea un issue (Historia, Tarea, Subtask…) |
| `POST` | `/api/tickets/{parentKey}/subtasks` | Crea varias subtareas bajo un issue, en orden |
| `GET` | `/api/tickets/{key}` | Estado actual → `{ "key", "url", "summary", "status", "dueDate" }` |
| `POST` | `/api/tickets/{key}/transition` | Mueve el issue a un estado por nombre: `{ "status": "Congelamiento" }` |
| `POST` | `/api/tickets/{key}/comments` | Agrega un comentario: `{ "body": "texto" }` (cada línea, un párrafo) |
| `POST` | `/api/tickets/{key}/links` | Enlaza con otro issue: `{ "targetKey": "GRUP-2", "type": "Relates" }` |
| `PATCH` | `/api/tickets/{key}` | Actualiza la fecha de vencimiento: `{ "dueDate": "2026-11-15" }` (`""` la borra) |

`POST /api/tickets`

```json
{
  "summary": "[REQ-002] Resolver Vulnerabilidades de Checkmark de EPSM",
  "description": "Texto; cada línea es un párrafo",
  "issueType": "Historia",
  "projectKey": "GRUP",
  "sprintId": 1,
  "parentKey": null,
  "priority": "High",
  "labels": ["pase"]
}
```

Además acepta `dueDate` (`yyyy-MM-dd`), `relatesTo` (crea un enlace "Relates" con ese issue) e `initialStatus`
(mueve el issue a ese estado justo después de crearlo). Solo `summary` y `description` son obligatorios. `projectKey` por defecto `Jira__ProjectKey`; sin `sprintId` el issue
queda en el backlog. Respuesta `201`: `{ "key": "GRUP-2", "url": "https://intellihack.atlassian.net/browse/GRUP-2" }`.

`POST /api/tickets/GRUP-2/subtasks`

```json
{ "subtasks": [ { "summary": "T-01 · …", "description": "Cubre AC-01" } ], "labels": ["pase"] }
```

Respuesta `200`: `[{ "key": "GRUP-3", "url": "…" }, …]` en el mismo orden.

### Proyecto GRUP (Grupo009)

Team-managed. Tipos de issue: `Historia`, `Tarea`, `Subtask` (requiere `parentKey`), `Epic`, `Error`. Tablero 2 con
sprints habilitados; el campo de sprint es `customfield_10020`. Un issue solo entra a un sprint activo o futuro.

### Proyecto OCD

Jira Software, team-managed, tipo de issue `OCD`. Su flujo tiene los 7 estados del OCD además de los 3 por defecto
(que siguen usando `Epic` y `Subtask`): **Validación → Congelamiento → Reversión de congelamiento → Recongelamiento →
Calendarización → Aprobación → Producción**, con transiciones globales (el orden lo valida el checklist de Pase).
Un OCD se crea con `projectKey: "OCD"`, `issueType: "OCD"`, `relatesTo: <Historia>` e `initialStatus: "Validación"`;
cada transición se registra con un comentario del checklist y la fecha de pase va en `dueDate`.

