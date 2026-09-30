# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Candidate Screening Loader

Aplicación de escritorio para Windows que procesa por lotes los CVs de una búsqueda laboral contra el agente
`serenityapp-candidate-screening-studio` del AI Hub. Existe porque la micro app Candidate Screening Studio
acepta 5 archivos por ejecución, y Recursos Humanos necesita procesar cientos.

No modifica el agente, ni el dataset, ni la micro app: es un cliente más de la misma API. El agente escribe
las filas de candidatos él mismo, y la micro app las muestra.

El vocabulario que ve el usuario **no es el del código**: una `Batch` no aparece nunca en pantalla y un job
opening se llama *oferta*.

## Commands

> **Windows / PowerShell.** Al ejecutar por herramienta, usar la **herramienta PowerShell** — la de Bash
> corre `/usr/bin/bash` y descarta silenciosamente las asignaciones `$env:`.

```powershell
# Build — el orden importa: la interfaz se embebe en el ejecutable
pnpm -C ui build
dotnet build src/ScreeningLoader.sln

# Publicar el ejecutable distribuible
pnpm -C ui build
dotnet publish src/ScreeningLoader.Shell -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Desarrollo: el shell apunta al dev server y hay hot reload de la interfaz
pnpm -C ui dev
```

## Arquitectura

Tres piezas, un repo:

- **`src/ScreeningLoader.Core`** — el motor. Sin UI, sin dependencias de ventana. Expone una corrida como un
  flujo de `RunEvent` que cualquier host consume, y `ScreeningLoaderEngine` como única superficie pública.
- **`src/ScreeningLoader.Shell`** — la ventana. Un `Form` de WinForms cubierto al 100% por un control
  WebView2, más el puente `PostWebMessage`. **Nada de WinForms es visible**: el marco existe por el borde, el
  título y el ícono, y lo único que se usa de la librería además de eso es el diálogo nativo de carpeta.
- **`ui/`** — la interfaz en React, sobre el design system de Serenity.

**`src/ScreeningLoader.Console`** es el host de desarrollo del motor. No se distribuye.

## La interfaz

**La clase del tema oscuro es `serenity-dark`, no `dark`** — así lo define el `@custom-variant` de los tokens.
Usar `dark` no rompe nada de forma visible, simplemente no hace nada.

## Errores

Toda excepción del motor deriva de `ScreeningLoaderException` y **declara su `ErrorKind`**. La clase del error
es una propiedad del error, no del lugar donde se atrapa:

- **`Transient`** — se reintenta con backoff. 429, lock del dataset, timeout de red.
- **`File`** — ese CV va a `fallidos/` y la corrida sigue. Ilegible, no es un CV, falló su INSERT.
- **`Fatal`** — la corrida aborta. Credenciales, falta de permiso de Execution, agente sin configuración.

Un `catch` no decide política: la lee del `ErrorKind`. Sin esta regla, la diferencia entre "reintentá" y
"descartá este CV" termina resuelta caso por caso, que es cómo se pierde un candidato en silencio.

## Datos personales

Los CVs son datos personales de candidatos reales. Dos reglas que no son negociables:

**Nunca registrar la sentencia SQL ni el cuerpo de la respuesta del dataset.** El mensaje de un INSERT que
falla arrastra la fila completa: nombre, mail, teléfono y las observaciones enteras. Se registra la operación
y el estado, nunca el contenido — ver `ErrorLog.Describe`.

**El log de auditoría no lleva datos del candidato.** Nombre de archivo, resultado, score y momento. Nunca el
nombre de la persona, su mail, su teléfono ni una línea de sus observaciones.

## El dataset

Se accede sólo a través del skill del plugin, y el plugin tiene reglas propias:

- **Una sentencia por llamada.** No hay batching ni transacciones.
- **Nunca `SELECT *`** — antepone una columna interna y devuelve el resto igual. Las columnas se nombran.
- **Los nombres de tabla son `Jobopenings` y `Candidates`**, con esa capitalización exacta. El nombre lógico
  `job_openings` no existe: el plugin lo aplana y una consulta contra él no encuentra nada.
- **Los datetime vuelven como `"2026-08-24 09:05:41"`**, texto plano sin zona.
- **Una consulta sin coincidencias vuelve 200 con `rows` vacío**, no 404 — verificado contra el Hub. El 404
  del endpoint de skills significa que el código de la skill no existe, así que **no se traduce a lista
  vacía**: hacerlo escondería un `datasetSkillCode` equivocado detrás de "esta organización no tiene
  búsquedas". La micro app sí tiene esa traducción; en el camino directo al Hub esa rama no se dispara.
- **Una sentencia malformada vuelve 500**, no 400, y el cuerpo trae la sentencia adentro. Un 500 se trata
  como transitorio, así que una consulta rota se reintenta hasta agotar los intentos antes de fallar.

Todo literal SQL se construye con `Sql.*`. Los identificadores nunca se interpolan; los helpers lanzan en vez
de coercionar.

**El dataset es SQLite: un solo escritor a la vez.** Por eso los lotes son secuenciales por defecto.

## La plataforma

- **El recibo de una ejecución no trae el nombre del archivo.** La correspondencia archivo ↔ fila **se
  consulta** (`SELECT id, cv_file_name FROM Candidates WHERE id IN (...)`), nunca se infiere por conteo.
  Inferirla archiva CVs que no tienen fila y deja pendientes los que sí.
- **`processEmbeddings` viaja siempre explícito.** Hay tres defaults distintos en juego: el endpoint directo
  del Hub usa `false`, el de Nexus `true` y el SDK oficial `true`.
- **Una subida devuelve 200 antes de que el archivo esté procesado.** Sólo los que llegan a `success` viajan a
  una ejecución; uno sin procesar produce un candidato armado sobre un documento vacío.
- **El JWT dura 15 minutos** y una corrida dura mucho más. Se refresca por anticipado, antes de cada lote, no
  en reacción a un 401.
- **El SDK oficial `SubgenAI.SerenityStar.SDK` no se puede usar acá**: exige una API key en todos sus caminos
  de construcción y esta app autentica con usuario y contraseña. Sí sirve como referencia de las formas del
  wire.

## Comentarios

Comentar poco. La mayor parte del código se lee sin comentarios: los buenos nombres y una estructura clara
cargan la intención. Un comentario se justifica sólo cuando el código no puede explicarse solo: un *por qué*
no obvio, una restricción, una trampa, o una decisión que un lector cuestionaría.

Escribir cada comentario para **alguien que lee el diff en un PR** y no participó de la conversación que
produjo el código.

- **Por defecto, ninguno.** Antes de agregar uno, asumir que no hace falta.
- **Explicar el *por qué*, nunca el *qué*.** No narrar lo que hace la línea siguiente.
- **Una línea, lenguaje llano.** Sin relatos de varias líneas ni tono conversacional.
- **Sin residuo de conversación.** Nunca referenciar el prompt, un intento anterior, una alternativa
  descartada, ni "decidimos…". Tampoco documentos de diseño: el comentario describe el
  código como está, no cómo llegó ahí.
- **Sin divisores ni narración de secciones.** Si un bloque necesita título, extraer una función con un buen
  nombre.
- **Borrar el código comentado.**

Los summaries de XML doc describen el rol del miembro y nada más.
