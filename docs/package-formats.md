# Package Formats

## Plugin Package

`.onedesk-plugin` is a zip-compatible package.

Required:

- `onedesk.plugin.json`
- backend artifacts when backend plugin behavior exists
- frontend logic files when frontend plugin behavior exists

Plugins do not ship custom UI. Settings are declared through JSON Schema plus OneDesk extension fields.

## Component Package

Component packages are zip-compatible packages and include:

- `onedesk.component.json`
- Vue 3 component files
- visual editor configuration when visual editing is supported
- dependent actions

## Scheme Package

Scheme packages include:

- scheme manifest
- pages
- components
- actions
- required plugin dependency packages

During scheme import, missing plugins are installed from the package. Version conflicts are shown to the user for a decision.
