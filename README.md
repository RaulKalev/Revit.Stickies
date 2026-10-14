<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/icon-dark.png">
    <img src="docs/images/icon-light.png" width="40" alt="">
  </picture>
  Stickies
</h1>

Sticky notes for drawing review in Revit.

Put a numbered note on any plan, section, elevation or 3D view: *"Camera too close to the door"*,
*"Missing card reader"*. Everyone working on the project sees it in their own Revit, can comment on it and mark it
done. No sign-in and no server: the notes are small files in the project's Dropbox folder.

The notes are drawn on top of the view and never become part of the model. They don't print, don't export, and can
never end up on a client drawing.

<p>
  <img src="docs/images/issue-list.png" width="300" alt="The Stickies pane with a list of issues">
  &nbsp;
  <img src="docs/images/issue-details.png" width="300" alt="An issue open in the Stickies pane">
</p>

## Install

Stickies works with **Revit 2024** and **Revit 2026** on Windows.

1. Get the `Stickies-<version>.zip` package and unzip it.
2. Close Revit and double-click **Install.cmd**. It sets up Stickies for every supported Revit version on the computer,
   for your Windows user only (no administrator rights needed).
3. Start Revit. Stickies is on the **RK Tools** tab.

To remove it, double-click **Uninstall.cmd**. To update, install the new package the same way.

## Getting started

Click **Stickies** on the ribbon to open the Stickies pane. You can dock it anywhere, like any other Revit panel.

The first time you open a project, Stickies asks where to keep its notes. Pick **Create next to the model** and a
`_markup` folder is made next to the model file. For a workshared model, that's next to the central file. Everyone
who opens the model from the same Dropbox folder then shares the same notes. If the model isn't in a folder (for
example a cloud model), choose the project's shared folder yourself. Stickies remembers it.

<img src="docs/images/first-time-setup.png" width="300" alt="Choosing where to keep the notes">

## Adding a note

1. Open the view where you see the problem.
2. Click **Add** in the pane.
3. Click the spot in the view, or select the element first if the note is about one element.
4. Give it a title, and if you like some details, a priority and who should look at it. Then click **Add issue**.

Stickies remembers the view as you saw it: its zoom, and for 3D views the camera angle and the section box (or that
there was none). Anyone who
**double-clicks** the note later gets exactly that view back.

<img src="docs/images/new-issue.png" width="300" alt="Adding a new issue">

## Working through notes

- **Click** a note to read it, edit it or add a comment.
- **Double-click** a note (or press Enter) to jump to its view.
- Move it along with **Start**, **Resolve** and **Reopen**. Whoever created a note **closes** it once they've checked
  the fix.
- A note shows *View not found* or *Element not found* when its view or element has been deleted from the model.

## Markers on views

Each note appears in the view as a numbered marker, in the colour of its status: blue for open, orange for in
progress, green for resolved, grey for closed. The note you have open gets a white ring. **Click** a marker to open
its note in the Stickies panel (the panel opens if it was closed); **double-click** it to jump to the view exactly as
it was when the note was made.

Markers show in the view where the note was made. Notes made in a 3D view also show in any other view that shows
their spot. Use the **eye** button
on the ribbon or in the pane to hide or show them.

<img src="docs/images/markers.png" width="620" alt="Numbered markers in their status colours">

### Notes as cards

Clicking a marker shows its note as a sticky-note card next to it. Turn on **Cards** (the note button at the top of
the pane) to see every note's card at once, next to its marker, with its title, the start of its details and who it is assigned to. Cards show only in the view the note was
made in, and only while markers are on. Click a card to open its note, double-click it to jump to its view. Like the markers, cards are never printed or
exported.

<img src="docs/images/cards.png" width="620" alt="Sticky-note cards next to their markers">

## Finding notes

Search by any word in a note, its comments, the view name or a person. Switch between **Active**, **Resolved**,
**Closed** and **All**. The filter button narrows the list by priority, author or assignee, and sorts it. The
markers in your views follow the same filter.

<img src="docs/images/filters.png" width="260" alt="Filters and sort">

Your filters, sort order and marker setting are yours: they're saved on your own computer, separately for each
project, and never shared.

## When two people edit the same note

Notes update live: when a colleague adds or changes one, it appears as soon as Dropbox has synced it. If you're editing a note that
someone else just changed, Stickies won't overwrite their change. It tells you, and **Reload** shows their version.

<img src="docs/images/someone-else-changed-it.png" width="300" alt="Someone else changed this issue">

## Light or dark

Switch between dark and light with the button in the top corner of the pane. The ribbon icon follows Revit's own
theme.

<img src="docs/images/light-appearance.png" width="300" alt="Light appearance">

## Where the notes are kept

```
P123 School/            the project's Dropbox folder, next to the model
  P123_EN.rvt
  _markup/
    project.json        which model these notes belong to
    active/             one small file per note
    archive/            done notes, one file per month (2026-10.json, ...)
```

Each note is a plain JSON file, so the folder can be backed up, synced or read by other tools.

**Tidying up.** Once a note is resolved or closed and nobody has touched it for a week, Stickies moves it from
`active/` into that month's archive file. That keeps the active folder small. Archived notes still show in the list
(under **Resolved**, **Closed** and **All**). Reopening one, or adding a comment, brings it back.

**Sync conflicts.** When two people change the same file before Dropbox has synced, Dropbox keeps both and names one
*"conflicted copy"*. Stickies never loads those copies. It tells you at the bottom of the pane, and **Open folder**
takes you there to compare or delete them.

## Keyboard shortcuts

While the Stickies pane has focus:

| Keys | Does |
| --- | --- |
| Ctrl+F | Search |
| Enter | Open the selected note's view |
| Ctrl+Enter | Save the open note, or post the comment you are typing |
| Esc | Close the open note, discard a new one, or clear the search |
| F5 | Reload the notes |

## Status

Stickies is in early development: the first version for daily use by the team.

---

### For developers

```
dotnet build Stickies.sln -c Release
dotnet test Tests/Stickies.Core.Tests -c Release
```

- `Stickies` is the add-in. It builds `Stickies.dll` for Revit 2024 (`net48`) and Revit 2026 (`net8.0-windows`) as one
  merged DLL each. Local builds go to `%UserProfile%\Desktop\DevDlls\Stickies\`. Set `StickiesUseDefaultOutput=true`
  to use `bin\` instead.
- `Stickies.Core` holds the data format and rules, with no Revit dependency. It is unit tested.
- `Tests/UiHarness` runs the real pane outside Revit and writes screenshots to `Tests/UiHarness/out/`.
- `Tools/Build-Release.ps1` builds both versions and packs `artifacts/Stickies-<version>.zip` with the installer
  (`Deploy/Install.cmd`, `Uninstall.cmd`, `Install-Stickies.ps1`).
- Hot reload (ricaun AppLoader) works for everything except `Stickies/Shell/PaneShell.cs`, which needs a Revit
  restart.
