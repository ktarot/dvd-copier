# ODD Utils — new repository staging

This branch is an integration/staging source for the new GitHub repository `ktarot/odd-utils`. Do not merge this branch into the existing `dvd-copier` main branch unless that is separately requested.

## Functional scope preserved
- Optical drive detection; display CD / DVD / BD family where the device name allows it
- ODD disc readiness and drive-letter display
- Tray open / close (hardware dependent)
- Folder selection and optional new folder creation
- Copy with directory structure, collision checks, progress, cancellation, and completion notifications
- 10-block LED dot-matrix progress display and Light/Dark theme
- Standalone Windows x64 EXE produced with GitHub Actions

## Added features
- Expandable audio drawer in the **same** 360×640 default window
- Responsive row allocation, readable status messages, scrollable player at higher DPI
- Playback from local or optical-drive MP3/M4A/WAV/WMA/FLAC/AAC/AIFF files (codec availability dependent)
- Audio CD-DA playback via Windows legacy MCI commands (drive-dependent; compatibility not guaranteed)
- Track selection, previous/next, play/pause/stop, seek by clicking LED progress blocks
- Local sidecar lyrics (`.lrc` and `.txt`), with timed LRC display
- Conflict prevention between disc copying and audio playback

## New GitHub repository
Repository creation must be performed from GitHub: https://github.com/new
1. Owner: `ktarot`
2. Repository name: `odd-utils`
3. Choose Private or Public.
4. Do not initialize with files if source is going to be added through the GitHub connection.
5. After creation, transfer the source from `feature/odd-utils-responsive-drawer` into the new repository and run its Actions workflow.

## Important limitations
- Successful build does not prove CD-DA compatibility, audio codec availability, or hardware operation.
- No online lyrics lookup or embedded metadata lyrics extraction.
- Standard file copying does not extract CD-DA audio from a music CD.
- For audio playback, NAudio 2.2.1 is restored during building; the self-contained EXE is intended to run without requiring separate .NET installation.
