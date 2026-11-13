# Backend .NET 10 – creación de tickets Jira

Variables de entorno (token en https://id.atlassian.com/manage-profile/security/api-tokens):

```
Jira__Email=tu@correo.com
Jira__ApiToken=xxxx
# opcionales: Jira__BaseUrl, Jira__ProjectKey (KAN), Jira__IssueType (Task)
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

Variables opcionales: `APP_NAME`, `PLAN_NAME`, `LOCATION`, `RESOURCE_GROUP`, `CORS_ORIGIN` (URL del frontend), `JIRA_EMAIL`. Los nombres predeterminados son estables para reutilizar recursos en posteriores despliegues. Un grupo puede contener recursos de distintas regiones.

### Alternativa B1 de pago autorizada

Ejecuta `bash deploy-b1.sh` desde el paquete extraido. Selecciona Suscripcion Dev, Central US y un plan B1 Linux de una instancia, con nombres fijos para reutilizarlo. No actualiza un plan F1 existente a pago: utiliza `plan-jira-b1-centralus`.

Precio publico consultado: USD 0.018/hora, aproximadamente USD 13.14 por 730 horas, sin impuestos ni posibles extras. La facturacion depende del contrato. Detener la app no detiene el cargo del plan. Si falla el despliegue despues de crear el plan, este puede seguir generando cargos; revisa los recursos en Azure. Antes de eliminarlo, verifica que no aloje otras aplicaciones.

La API de uso/cuotas puede mostrar limites distintos a los aplicados durante la creacion. Un limite publicado de 30 no garantiza capacidad ni aprobacion; si Azure rechaza B1, el script termina sin probar otros SKU.

Si Azure rechaza F1 por cuota o politica, el script termina con el error: no cambia automaticamente a un plan de pago. Puedes solicitar cuota o probar una region permitida con `LOCATION=centralus bash deploy.sh`; no se garantiza disponibilidad por suscripcion. F1 tiene limites de CPU y arranque en frio.

Para regenerar `app.zip` tras cambios, ejecuta desde esta carpeta `dotnet publish -c Release -o bin/azure-publish --self-contained false` y comprime el **contenido** de esa salida (sin carpeta contenedora).

La API sigue sin autenticacion: no compartas su URL publicamente; CORS no impide llamadas externas para crear tickets.

## Contrato (idéntico al backend Java)

`POST /api/tickets`

```json
{ "summary": "No puedo acceder al portal", "description": "Detalle...", "issueType": "Historia", "priority": "High", "labels": ["chatbot"] }
```

`issueType` es opcional (por defecto `Jira__IssueType`, "Tarea"). En este proyecto los tipos válidos son `Tarea`, `Historia`, `Epic`, `Subtask`.

`201`: `{ "key": "KAN-12", "url": "https://intellihack.atlassian.net/browse/KAN-12" }`
Error de Jira: `502` con `jiraStatus` y `detail`. `GET /api/health` para verificar.
