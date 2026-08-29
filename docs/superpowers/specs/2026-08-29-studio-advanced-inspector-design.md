# Studio Advanced Inspector Design

## Goal

Make the Scene Hierarchy and Inspector comfortably usable by default, and turn the Inspector into a primary, schema-driven authoring surface without introducing component-specific engine UI.

## Layout

- Increase the default Scene Hierarchy width from 290 to 340 pixels.
- Increase the default Inspector width from 370 to 460 pixels.
- Give the Authoring preset 360/500 pixel hierarchy/Inspector widths and the Debug preset 330/460 pixel widths.
- Bump the saved layout version. Migrate the three known legacy preset width pairs to the new values, while preserving any widths that a user changed manually.
- Keep both columns resizable and preserve the existing visibility and layout persistence behavior.

## Inspector Information Architecture

The Inspector becomes a three-part panel:

1. A selection header shows the entity name, stable entity ID, and attached component count.
2. A searchable attached-components browser presents each component as a structured card. Cards show display name, exact type, schema status, description, and defined properties with their values and value kinds. Selecting a card selects that component for editing.
3. A schema-driven editor exposes the selected component description, component add/replace/remove actions, property selection, type/range/allowed-value help, a value editor, and set/reset actions.

The attached component filter is case-insensitive and matches component display name, exact type, description, property names, and property values. Filtering never changes authored data. When a selected card disappears due to filtering, the first visible card becomes selected; clearing the filter restores the complete attached-component list.

## Data Flow

The existing `RekallAgeInspectorModel` remains the authoritative source. Studio projects its attached components into an observable filtered collection for the World workspace. The selected card synchronizes the existing `ComponentTypeInput`, so all edits continue through the existing generic `rekall.entity.component.*` and `rekall.entity.property.*` command paths. No component-specific behavior is added.

Scene selection or model reload refreshes the card collection, selection summary, available schemas, and typed property choices together. Existing `InspectorLines` remain available for automation and compatibility but are no longer the primary visual presentation.

## Error and Empty States

- With no entity selected, show the existing explicit selection guidance and disable edit actions through their existing command predicates.
- With an entity selected but no attached components, show an actionable empty state while retaining the Add Component editor.
- With no search matches, say so without clearing the selected entity or modifying components.
- Unknown component/property schemas remain editable as raw JSON and are labeled as custom/unregistered rather than hidden.

## Verification

- Layout tests prove new defaults, legacy migration, and preservation of custom widths.
- View-model tests prove component projection, search matching, selection synchronization, and empty/no-match behavior.
- XAML structure tests prove the selection header, search box, component cards, metadata, and editing controls remain present.
- A focused Studio build and live visual check verify the Inspector at normal and minimum supported window sizes with Summit Run loaded.

