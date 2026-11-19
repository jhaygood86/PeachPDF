# Form values and visibility that a script or a test harness supplies

A viewer that runs the document's JavaScript, or a test tool that types into fields, shows things the file does not contain: `password`
(no `/V` at all; the harness types the text and shows the caret), `bug_113910` (`/AA` formatting scripts), the second field of
`bug_733528`, and `bug_42271472` (a highlight with `/F 2` hidden that an `OpenAction` script sets visible). This reader does not run
scripts and does not reproduce the form-fill environment's caret, combo box drop-down button or scroll bars, so those pages stay as the file
left them. A script engine would be a separate project (a security surface as well), and drawing chrome is a viewer's UI concern rather than
the document's; neither has a spec deviation to file. The PDFium runner counts these tests as blank.
