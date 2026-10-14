# Stickies data format

Stickies keeps a project's notes as plain JSON files in a `_stickies` folder, normally next to the model (the central
file for workshared models). The files are meant to be synced (Dropbox), backed up, and read by other tools, for
example to load statistics into a database.

```
<project folder>/
  _stickies/
    project.json            which model the folder belongs to
    active/                 one file per note: <id>.json
    archive/                done notes, one file per month: YYYY-MM.json (a JSON array of notes)
    compact.lock            present only while a Stickies session is archiving
```

Personal settings (filters, sort, marker and card visibility, a chosen folder) are never stored here. They live on
each computer in `%AppData%\Stickies\projects\<model id>.json`.

## Conventions

- UTF-8 JSON, camelCase property names.
- Every file starts with `schemaVersion` (currently `1`). Readers ignore properties they don't know, so the format can
  grow. A version of Stickies never overwrites a note written by a newer version.
- Enum values are camelCase strings: status `open` | `inProgress` | `resolved` | `closed`, priority
  `low` | `normal` | `high`.
- Times are ISO 8601 with the local offset, to the second: `2026-10-14T09:30:12+03:00`.
- Lengths and points are in **metres**, in the model's internal coordinates. Directions are unit vectors.
- Views and elements are referenced by Revit **UniqueId**, never ElementId.
- People are Windows user names (no sign-in).

## project.json

```json
{
  "schemaVersion": 1,
  "modelGuid": "22487c0e-321d-4e8c-be66-04fce934fdb7-00000687",
  "projectName": "P123_EN"
}
```

`modelGuid` is the model's `ProjectInformation.UniqueId`, which the central model and its local copies share.

## A note: active/&lt;id&gt;.json

```json
{
  "schemaVersion": 1,
  "id": "20261014-093012-a3f9c1",
  "number": 12,
  "title": "Camera too close to door",
  "body": "Move VVS-03 at least 500 mm from the frame",
  "status": "inProgress",
  "priority": "high",
  "author": "raul",
  "assignee": "joonas",
  "created": "2026-10-14T09:30:12+03:00",
  "modified": "2026-10-14T10:02:00+03:00",
  "resolvedBy": null,
  "resolvedAt": null,
  "location": {
    "point": [12.345, 6.789, 3.0],
    "elementUniqueId": "7a1c0e2b-...-00041f2a",
    "elementCategory": "Communication Devices"
  },
  "view": {
    "uniqueId": "...",
    "name": "EN_2_korrus_VVS",
    "type": "FloorPlan",
    "sectionBox": null,
    "orientation": null,
    "zoomCorners": [[0.0, 0.0, 0.0], [30.0, 20.0, 0.0]]
  },
  "comments": [
    { "author": "joonas", "at": "2026-10-15T08:00:00+03:00", "text": "Moved it 600 mm to the left." }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `id` | `yyyyMMdd-HHmmss-xxxxxx`: creation time plus 6 random hex digits. Also the file name. |
| `number` | The number on the marker: one more than the highest in the folder when the note was made. Optional. |
| `status`, `priority` | See *Conventions*. Anyone may start, resolve or reopen a note; only its `author` closes it. |
| `resolvedBy`, `resolvedAt` | Set when the note is resolved (or closed without being resolved); cleared when reopened. |
| `location.point` | Where the marker sits. `elementUniqueId` / `elementCategory` are set when the note is about one element. |
| `view.type` | Revit view type: `FloorPlan`, `CeilingPlan`, `Section`, `Elevation`, `ThreeD`, ... |
| `view.sectionBox` | 3D views: `min`, `max` (metres, in the box's own coordinates) and `transform` (12 numbers: origin, basisX, basisY, basisZ). `null` when the view had no section box. |
| `view.orientation` | 3D views: `eye` (metres), `forward`, `up`. |
| `view.zoomCorners` | 2D and orthographic 3D views: lower-left and upper-right corners of what was on screen. |

## How files change

- A note is written atomically: to a temporary `*.tmp` file next to it, then renamed over the old file. Readers skip
  `*.tmp` files.
- A change rewrites only that note's own file. Stickies refuses to save over a note that someone changed after it was
  opened.
- Files that are not named like a note (for example Dropbox *"conflicted copy"* files) are never loaded, only
  reported.
- **Archiving:** a note that is resolved or closed and unchanged for 7 days is merged into
  `archive/<month it was resolved>.json` and then removed from `active/`. Only one session archives at a time (the one
  holding `compact.lock`). A note can be in `active/` and in the archive at once (it was reopened); the `active/` copy
  is the current one.
