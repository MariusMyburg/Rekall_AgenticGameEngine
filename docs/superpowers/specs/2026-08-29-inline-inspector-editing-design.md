# Inline Inspector Editing Design

## Goal

Every property displayed in a Studio Inspector component card is directly editable in that row with a control appropriate to its schema type. Selecting the same property again in a lower dropdown is not part of the normal workflow.

## Architecture

Keep Editor inspector contracts as immutable read models. Studio wraps each visible property in a mutable editor-row model that owns draft state, local parse/range feedback, schema choices, and native JSON conversion. Parameterized row Commit and Reset commands route through the existing `rekall.component.set_property` and `rekall.component.remove_property` commands, preserving validation, transactions, undo/redo, persistence, and preview refresh.

## Editors

- Boolean: checkbox, immediate semantic commit.
- Number/integer: validated invariant numeric field; Enter/focus loss commits, Escape restores.
- Allowed values: dropdown.
- Asset/entity references: filtered searchable dropdown using stable IDs.
- Color: canonical hex field plus swatch/channel editing.
- Vector2/3/4: labelled numeric component fields committed as one native JSON array.
- String: inline text field.
- Structured/unknown: expandable JSON editor with explicit Apply.

Undefined schema properties remain visible and become defined on commit. Invalid drafts remain visible after local or server rejection and never mutate the scene. The lower panel retains component add/replace/remove and becomes an explicitly labelled advanced custom-JSON fallback.

## Acceptance

- Existing component-card rows are directly editable without using the lower property selector.
- Accepted changes persist native JSON, create one transaction, refresh the viewport, and work with Undo/Redo.
- Invalid number/color/vector/JSON drafts show inline errors, remain editable, and leave the scene unchanged.
- Component management and unknown custom property authoring remain available.

