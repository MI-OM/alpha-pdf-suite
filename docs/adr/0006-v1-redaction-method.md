# ADR-0006: v1 redaction method and verification

**Status:** Proposed (2026-11-01)
**Decides:** how v1 removes content the user redacts, how the result is checked before it is saved, and what v1 does not attempt.

## 1. Context and constraints
- **What redaction must do** (PRD §4.7): remove underlying content, not cover it.
- **The v1 method sketched by the PRD:**
  1. Rasterise every page with a redaction area, and replace it with its image, the areas filled.
  2. Write a new file with a full rewrite.
  3. Scrub metadata, attachments, scripts, hidden layers, and structure-tree text.
  4. Re-extract text, images and metadata, and refuse to save if any redacted content is found.
- **UI copy:** before applying, the UI must say that affected pages lose selectable text and vector quality.
- **Acceptance tests** must inspect the saved output for every recoverable representation (PRD §8).
- **No permissively licensed library** offers redaction that keeps vector content (PRD §4.7; iText's pdfSweep is AGPL).

## 2. Options considered
- **A. Rebuild:**
  - render each affected page and fill the areas
  - build a new document from those images and the unaffected pages
  - scrub with qpdf
  - verify independently
  - This is the PRD's method.
- **B. Edit in place:** remove content objects under the areas from the original file, then rewrite it. That keeps vector content and text, but every leftover representation of the content must be found: shared form XObjects, images, alternate text.
- **C. Glyph-level rebuild:** rebuild text objects without the redacted glyphs (the probe in S5), plus image pixel scrubbing. Promising, but it does not yet cover all font types, images, or vector art.
- **D. Cover only** (draw boxes). Not redaction (PRD §8); used only as a negative control.

## 3. Evidence collected
Spike item S5 (spike results: S5 evidence, and findings 30–37):
- **Canary coverage:**
  - Seven canary fixtures carry 13 canaries in every location the spike plan lists: text, image, vector art, shared form XObject, annotation, Info and XMP metadata, attachment, JavaScript, bookmark, structure-tree alt text, hidden layer, and an earlier revision.
  - After redaction at each manifest target, no canary is found by PDFium text extraction or by the byte-level search of strings, content streams and other streams.
  - **The one designed exception:** the shared XObject's canary stays on the page the user did not redact, and the verifier reports it as a warning.
- **Negative controls:** all 7 are rejected, each by the check meant for it:
  - a box over the text
  - an incremental save
  - an incremental update after a correct redaction
  - metadata left behind
  - an annotation left on a redacted page
  - a page outside the page tree
  - an area that was never filled
- **The verifier's checks:**
  - a single revision
  - pages that match the source
  - no document-level content
  - no stray pages
  - each redacted page exactly one image
  - text removed
  - areas rendered solid
- **Behaviour:** the output is deterministic, and unaffected pages keep their text.
- **Cost:**
  - Redacting 2 pages of the 2,000-page fixture took 5.5 s, of which 4.7 s was verification.
  - Each redacted page adds about 230–560 KB.
- **Verified on all three OSes:** CI run 41338226605.
- **Findings that shape the method:**
  - A raw byte search cannot verify redaction (finding 31).
  - PDFium extracts text from hidden layers (finding 32).
  - The verifier's limits fail closed (finding 35).
  - The glyph-level probe works for fonts that map back from Unicode (finding 36).
  - Redaction must move into the worker (finding 37).

## 4. Decision and rationale
**Proposed:** the rebuild method (A) is v1's redaction.
1. **Rendering:** each page with an area is rendered at 3 px/pt (216 dpi, at most 40 megapixels a page), with annotations included. The areas are filled black, rounded outwards, and the page becomes one image. Unaffected pages are copied unchanged.
2. **Scrubbing,** by qpdf, which writes one revision:
   - the catalog keeps only `/Type` and `/Pages`
   - `/Info` and the file ID are removed
   - page actions, page metadata, private application data and beads are removed
   - annotation actions, page links, reply links, and links that lead outside the output are removed
3. **The independent verifier runs on every output,** and a file that fails is never saved. Secrets are the source's text under each area, plus any terms the user adds.
   - **Warning, not refusal:** a secret that also appears on a kept page, which the UI must explain.
   - **Refusal:** a secret found anywhere else in the output.
4. **Redaction and verification run in the worker** (ADR-0003). That is Milestone 1 work.
5. **Before applying,** the UI states that redacted pages lose selectable text, links and vector quality, and grow in size (finding 30).
6. **OCR** to restore searchable text outside the areas (PRD §4.7, step 2) is optional, and follows the OCR milestone.
7. **For v2:** the glyph-level route (C), using our own content-stream parsing to keep character codes, with the same verifier as the gate.

**Rationale:**
- The method removed every canary the plan asked for.
- The verifier caught every broken output it was given, without trusting how the file was made.
- It uses only PDFium and qpdf, both permissive.

## 5. Trade-offs
- **Redacted pages become images:** no text, links or vector sharpness, and larger files.
- **Characters partly inside an area** are rendered away but not checked (finding 35).
- **The verifier can refuse a correct redaction** when a secret's text happens to occur elsewhere in the file, for example in a form value. It fails closed.
- **The verifier's known blind spots** (finding 31):
  - text in names or inline image data
  - Type 3 glyph procedures, which are searched raw
  - fonts whose codes are not their characters, where only text extraction covers drawn text
- **Verification dominates the cost** on large documents.

## 6. Licensing and security consequences
- **Licences:** only PDFium and qpdf (ADR-0002). No new licences.
- **Never in the app:** redaction parses untrusted files with both engines, so it must not run in the app process (finding 37).
- **The verifier is a security control.** Changes to it need negative controls proving the new check can fail (finding 29).
- **Determinism:** output carries no random ID, so two redactions of the same input are identical (finding 34).

## 7. Reversibility and migration cost
- **Where the code lives:** in `PdfSuite.Pdf.Redaction`, about 760 lines, with the redactor and the verifier as separate classes.
- **A v2 method** can replace the redactor and keep the verifier and its fixtures unchanged.

## 8. Validation experiment
- **Done in the spike:** the canary fixtures and negative controls, on all three OSes.
- **Not done yet:**
  - a real-world corpus: scanned and born-digital files, CJK fonts, Type 3 fonts, forms, and tagged PDFs
  - fuzzing the content-stream lexer and the verifier
  - redaction running in the worker, with the UI flow and copy
  - performance on documents with many redacted pages
  - OCR restoration of the remaining text
  - an external review of the method before the feature is called complete

## 9. Conditions that would cause reconsideration
- **The verifier refuses too many real documents** because of false matches, so it needs a more precise secret model.
- **Users need vector quality and text** on redacted pages in v1. The glyph-level route would then move forward.
- **A document feature is found that the scrub policy does not cover** and the verifier misses. Add a canary fixture and a check.
- **A permissively licensed library** appears that provides verified glyph-level redaction.
