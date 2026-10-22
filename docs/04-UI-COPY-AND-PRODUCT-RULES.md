# Open-Source PDF Suite — UI Copy and Product Rules

## 1. Brand and identity

Working product name: **Alpha PDF Suite**

This is a temporary descriptive name, not a confirmed final brand. Before choosing a permanent name, check trademark databases, domain availability, package identifiers, and potential confusion with existing software. An unrelated commercial PDF product named "AlphaPDF" (alphapdf.com) already exists, so this name must be replaced before any public release.

### Short description

A free, open-source PDF application for reading, organizing, annotating, editing, converting, and protecting documents on Windows, macOS, and Linux.

### Tagline candidates

- Your documents. Your device. Your freedom.
- A complete PDF workspace, built in the open.
- Powerful PDF tools. No subscriptions. No lock-in.

Do not use Adobe branding or imitate its distinctive product identity.

## 2. First launch

### Welcome screen

**Heading:** Welcome to your PDF workspace

**Body:** Open, read, organize, and work with PDF documents on your device. Your documents stay local unless you choose an action that requires an external service.

**Primary button:** Open a PDF

**Secondary button:** Learn the basics

**Privacy note:** No account is required to use the core application.

### Empty workspace

**Heading:** Start with a document

**Body:** Open a PDF from your device to begin reading, searching, annotating, or organizing its pages.

**Primary button:** Open PDF

**Secondary action:** Open recent documents

If no recent documents exist, hide the recent-documents action or show an empty-state explanation.

## 3. Main navigation

Suggested menu structure:

### File
- Open PDF…
- Open Recent
- Save
- Save As…
- Export…
- Print…
- Document Properties
- Close Document
- Quit

Only display Save when the current document or supported changes can be saved. Use context-aware labels when annotation state and document state differ.

### Edit
- Undo
- Redo
- Cut
- Copy
- Paste
- Select All
- Find…
- Preferences

Do not enable editing commands that the current selection or document cannot support.

### View
- Zoom In
- Zoom Out
- Fit Page
- Fit Width
- Actual Size
- Single Page
- Continuous
- Facing Pages
- Show Thumbnails
- Show Document Outline
- Full Screen

### Document
- Rotate Page
- Extract Pages…
- Insert Pages…
- Reorder Pages…
- Merge PDFs…
- Split PDF…
- Document Properties

### Annotate
- Highlight
- Underline
- Strikethrough
- Add Note
- Text Box
- Draw
- Shapes
- Show/Hide Annotations

### Tools
- OCR…
- Convert…
- Redact…
- Sanitize Document…
- Inspect Signatures…
- Form Tools

Only show or enable tools implemented in the current build.

### Help
- Keyboard Shortcuts
- User Guide
- Known Limitations
- Report a Bug
- Report a Security Vulnerability
- About

Security reporting should not expose confidential vulnerability details in a public issue tracker.

## 4. Reader toolbar

Suggested actions:
- Sidebar toggle.
- Previous/next page.
- Current page / total pages.
- Zoom out/in.
- Zoom preset.
- Search.
- Select tool.
- Hand/pan tool.
- Annotation tool selector.
- Print.
- More actions.

Tooltips:
- “Show or hide the document sidebar”
- “Go to the previous page”
- “Go to the next page”
- “Zoom out”
- “Zoom in”
- “Search this document”
- “Print this document”
- “Choose a tool”

## 5. Search panel

**Heading:** Find in document

**Placeholder:** Enter a word or phrase

**Controls:**
- Previous match
- Next match
- Match case
- Whole words

**Empty state:** Enter a word or phrase to search this document.

**No results:** No matches found. Try another search term or change the search options.

**Search limitation:** Some scanned documents contain images instead of searchable text. Run OCR to make supported scanned pages searchable.

Do not promise OCR will always recognize every word correctly.

## 6. Password-protected document

**Heading:** This document is password-protected

**Body:** Enter the document password to continue. The password is used to open this document and should not be shared.

**Field label:** Password

**Primary button:** Unlock PDF

**Secondary button:** Cancel

**Incorrect password:** The password could not unlock this document. Check the password and try again.

Never log the password. Do not transmit it to an external service.

## 7. Save and overwrite warnings

### Save As

**Heading:** Save a copy

**Body:** Choose where to save the updated document. The original file will remain unchanged.

**Primary button:** Save Copy

### Overwrite confirmation

**Heading:** Replace existing file?

**Body:** A file with this name already exists. Replacing it may overwrite its current contents.

**Primary button:** Replace File

**Secondary button:** Choose Another Name

### Unsaved changes

**Heading:** Save your changes?

**Body:** You have changes that have not been saved.

**Buttons:**
- Save
- Discard Changes
- Cancel

The application must not discard changes before the user chooses Discard Changes.

## 8. Page organization

### Merge PDFs

**Heading:** Merge PDF files

**Body:** Add PDF files and arrange them in the order you want. The originals will not be changed.

**Primary button:** Merge and Save

### Split PDF

**Heading:** Split document

**Body:** Choose how to divide this document. The application will create new files and keep the original unchanged.

### Delete page

**Heading:** Delete selected page?

**Body:** This page will be removed from the saved copy. The original file will remain unchanged unless you explicitly replace it.

Use the strongest warning only when the operation is genuinely destructive and not recoverable.

## 9. OCR workflow

**Heading:** Make scanned text searchable

**Body:** OCR identifies text in scanned pages. Recognition may contain errors, especially in low-quality scans.

**Controls:**
- Pages to process
- Recognition language
- Output location
- Preserve original appearance

**Primary button:** Start OCR

**Progress:** Recognizing text… {completed} of {total} pages

**Completion:** OCR finished. Review the recognized text before relying on it for important work.

**Cancellation:** OCR was cancelled. The original document was not changed.

The UI must reflect real progress and results; placeholders such as `{completed}` must be populated by the application.

## 10. Redaction workflow

**Heading:** Permanently redact sensitive content

**Body:** Redaction removes selected content from the output document. To do this reliably, each page that contains a redaction is converted to an image. Text on those pages can no longer be selected or searched unless you run OCR afterwards. Review the redactions and save to a new file before sharing.

**Primary button:** Review Redactions

**Final confirmation:** Apply redactions and create sanitized PDF?

**Body:** The new file will be checked automatically for any remaining text, images, or metadata from the redacted areas. If the check fails, the file is not saved. Review the saved file before distributing it.

Do not claim perfect removal of every possible representation. The implementation must be validated against the document types it supports.

## 11. Signature inspection

**Heading:** Digital signature details

Display separate fields for:
- Signature integrity.
- Signed content status.
- Signer certificate information.
- Certificate-chain validation.
- Revocation status.
- Timestamp information.
- Validation time and network limitations.
- Whether subsequent changes were detected.

Possible status messages:

**Valid integrity:** The signed content passed the available integrity checks.

**Trust not established:** The signature's integrity may be intact, but the signer’s identity or certificate trust could not be fully established.

**Modified after signing:** Changes were detected after signing. Review the signature details before relying on this document.

**Validation incomplete:** Some checks could not be completed. The result is inconclusive.

**Not evaluated (v1):** This version checks whether the signed content is intact. It does not yet check certificate trust, revocation, or timestamps.

In v1, the certificate-chain, revocation, and timestamp fields always show **Not evaluated**.

Do not use a single green checkmark to imply legal validity or full trust.

## 12. Security warnings

### External link

**Heading:** Open external link?

**Body:** This PDF contains a link to an external website. Only continue if you trust the destination.

**Buttons:** Open Link / Cancel

### Embedded file

**Heading:** This document contains an attachment

**Body:** Attachments may contain unsafe content. Save the attachment only if you trust its source.

**Buttons:** Save Attachment / Cancel

### Suspicious or unsupported action

**Heading:** Document action blocked

**Body:** The document requested an action that this application does not automatically perform.

Never claim malware detection unless a real, validated detection mechanism exists.

## 13. Error messages

### Cannot open file
“We couldn't open this PDF. The file may be damaged, unsupported, or inaccessible.”

Actions: Try Again / Choose Another File

### Unsupported feature
“This document uses a feature that is not fully supported yet. Some content may not display or behave as expected.”

### XFA form
“This document contains an XFA form, which this application does not support. The form may not display or work correctly.”

### Scripted form
“This form uses scripts to calculate or check values. This application does not run form scripts, so calculated fields will not update automatically.”

### Save failed
“We couldn't save the document. Your original file has not been intentionally replaced. Check the destination and available storage, then try again.”

Only state that the original remains intact when the implementation has actually guaranteed it.

### Operation cancelled
“The operation was cancelled. Review the output location before continuing.”

### Insufficient resources
“This document requires more resources than the application can safely allocate. Try a smaller document or process fewer pages.”

### Unexpected error
“Something went wrong while processing this document. The document's contents were not included in the error report.”

This last claim is allowed only if the logging implementation enforces it.

## 14. Settings

Suggested sections:

### Appearance
- Theme: System / Light / Dark
- Default zoom
- Default page layout
- Sidebar preferences

### Files
- Recent document history
- Clear recent history
- Default save behaviour
- Temporary-file cleanup

### Privacy
- Network activity information
- Clear local indexes and caches
- Crash reporting status, if implemented
- Optional update-check settings

### Accessibility
- Keyboard navigation
- High-contrast compatibility
- Reduced motion where relevant
- Text scaling and accessibility options

### Advanced
- OCR language packs
- Performance and resource limits
- Diagnostic logging
- Reset preferences

Do not expose a privacy toggle for telemetry or network behaviour that is not actually implemented.

## 15. About screen

**Product name:** Open-Source PDF Suite

**Description:** A free, open-source, cross-platform application for working with PDF documents.

**Licence:** Apache License 2.0 for original project code, subject to final project approval.

**Links:**
- Source Code
- Documentation
- Third-Party Licences
- Security Policy
- Report a Bug
- Acknowledgements

Display application version, build identifier, and relevant dependency information.

## 16. Copy and localization rules

- Use plain, direct language.
- Explain technical terms when they matter to a user decision.
- Do not shame users for errors.
- Do not claim perfect compatibility, security, accessibility, OCR, or conversion.
- Use consistent terms for documents, pages, annotations, and saved copies.
- Keep interface strings in resource files rather than scattering them through code.
- Support localization without embedding UI text in images.
- Make warnings specific to the action and consequence.
- Never use marketing copy to substitute for technical evidence.
