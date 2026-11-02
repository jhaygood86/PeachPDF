namespace PeachBrackets.Accessibility;

/// <summary>
/// What a piece of a document is to someone who reads it through an assistive technology rather than by looking at it: the roles of
/// WAI-ARIA 1.2, plus <see cref="Text"/> for the text itself. A native element has the role HTML gives it; a <c>role</c> attribute replaces it.
/// </summary>
public enum AccessibilityRole
{
    /// <summary>No role of its own: a container that adds nothing a reader needs. Not part of the tree; its content is.</summary>
    Generic,

    /// <summary>A piece of text. Its <see cref="AccessibilityNode.Name"/> is the text.</summary>
    Text,

    /// <summary>A message with important, time-sensitive information.</summary>
    Alert,

    /// <summary>An alert that interrupts the user's work to ask for a response.</summary>
    AlertDialog,

    /// <summary>A region declared as a web application rather than a document.</summary>
    Application,

    /// <summary>A self-contained composition, such as an article or a post.</summary>
    Article,

    /// <summary>Site-oriented content at the start of a page: the page's header.</summary>
    Banner,

    /// <summary>A quotation set off from the surrounding text.</summary>
    BlockQuote,

    /// <summary>A control that can be activated.</summary>
    Button,

    /// <summary>A caption for a table, figure or grid.</summary>
    Caption,

    /// <summary>A cell of a table.</summary>
    Cell,

    /// <summary>A control with two states, checked and not (or mixed).</summary>
    CheckBox,

    /// <summary>A fragment of computer code.</summary>
    Code,

    /// <summary>A cell that heads a column.</summary>
    ColumnHeader,

    /// <summary>A text box with a list of values to pick from.</summary>
    ComboBox,

    /// <summary>Content that supports the main content and stands on its own: a sidebar.</summary>
    Complementary,

    /// <summary>Information about the page at its end or about its parent: the page's footer.</summary>
    ContentInfo,

    /// <summary>The definition of a term.</summary>
    Definition,

    /// <summary>Content that is marked as removed.</summary>
    Deletion,

    /// <summary>A window over the page.</summary>
    Dialog,

    /// <summary>A list of references to members of a group, such as a table of contents.</summary>
    Directory,

    /// <summary>The whole document: the root of the tree.</summary>
    Document,

    /// <summary>Content that is stressed.</summary>
    Emphasis,

    /// <summary>A scrollable list of articles.</summary>
    Feed,

    /// <summary>A figure with a caption: an illustration, a diagram, a listing.</summary>
    Figure,

    /// <summary>A landmark with the controls that submit information.</summary>
    Form,

    /// <summary>A table whose cells can be focused and navigated.</summary>
    Grid,

    /// <summary>A cell of a grid.</summary>
    GridCell,

    /// <summary>A set of things that belong together but are not important enough for a landmark.</summary>
    Group,

    /// <summary>A heading; <see cref="AccessibilityNode.Level"/> says which.</summary>
    Heading,

    /// <summary>A graphic, an illustration or a picture.</summary>
    Image,

    /// <summary>Content that is marked as added.</summary>
    Insertion,

    /// <summary>A link, which has an address (or a place in the document) it leads to.</summary>
    Link,

    /// <summary>A list of items.</summary>
    List,

    /// <summary>A list of options to pick from.</summary>
    ListBox,

    /// <summary>An item of a list.</summary>
    ListItem,

    /// <summary>A log of messages in order, the newest last.</summary>
    Log,

    /// <summary>The main content of the page.</summary>
    Main,

    /// <summary>Text that scrolls past, with information that is not essential.</summary>
    Marquee,

    /// <summary>A mathematical expression.</summary>
    Math,

    /// <summary>A gauge of a value within a range, such as disk usage.</summary>
    Meter,

    /// <summary>A list of commands to choose from.</summary>
    Menu,

    /// <summary>A menu that stays visible, usually horizontal.</summary>
    MenuBar,

    /// <summary>An item of a menu.</summary>
    MenuItem,

    /// <summary>An item of a menu that can be checked.</summary>
    MenuItemCheckBox,

    /// <summary>An item of a menu of which only one can be checked.</summary>
    MenuItemRadio,

    /// <summary>A set of links for getting around the page or the site.</summary>
    Navigation,

    /// <summary>Content that is parenthetic or ancillary to the main content.</summary>
    Note,

    /// <summary>An item of a list box or a combo box.</summary>
    Option,

    /// <summary>A paragraph of text.</summary>
    Paragraph,

    /// <summary>An element whose meaning is purely visual: it is not part of the tree, and its content is.</summary>
    Presentation,

    /// <summary>A control that shows how far a task has got.</summary>
    ProgressBar,

    /// <summary>A control of which only one in its group can be checked.</summary>
    Radio,

    /// <summary>A group of radio buttons.</summary>
    RadioGroup,

    /// <summary>A landmark for content that is important enough to be found, with a name.</summary>
    Region,

    /// <summary>A row of a table, grid or tree grid.</summary>
    Row,

    /// <summary>A group of rows of a table.</summary>
    RowGroup,

    /// <summary>A cell that heads a row.</summary>
    RowHeader,

    /// <summary>A scroll bar.</summary>
    ScrollBar,

    /// <summary>A landmark for the page's search facility.</summary>
    Search,

    /// <summary>A text box for entering search terms.</summary>
    SearchBox,

    /// <summary>A divider that separates sections of content or groups of menu items.</summary>
    Separator,

    /// <summary>A control for choosing a value within a range.</summary>
    Slider,

    /// <summary>A control for choosing a value by stepping it up and down.</summary>
    SpinButton,

    /// <summary>A message with advisory information that is not important enough to be an alert.</summary>
    Status,

    /// <summary>Content that is of strong importance.</summary>
    Strong,

    /// <summary>Content that is a subscript.</summary>
    Subscript,

    /// <summary>Content that is a superscript.</summary>
    Superscript,

    /// <summary>A control with two states, on and off.</summary>
    Switch,

    /// <summary>A tab, which selects a panel.</summary>
    Tab,

    /// <summary>A table of rows and columns of cells.</summary>
    Table,

    /// <summary>The list of tabs.</summary>
    TabList,

    /// <summary>The panel a tab selects.</summary>
    TabPanel,

    /// <summary>A word or phrase with a definition.</summary>
    Term,

    /// <summary>A control for entering text.</summary>
    TextBox,

    /// <summary>A date, a time or both.</summary>
    Time,

    /// <summary>A timer that counts up or down.</summary>
    Timer,

    /// <summary>A collection of buttons or other controls.</summary>
    ToolBar,

    /// <summary>A contextual popup with information about another element.</summary>
    ToolTip,

    /// <summary>A list that can be expanded and collapsed.</summary>
    Tree,

    /// <summary>A grid whose rows can be expanded and collapsed.</summary>
    TreeGrid,

    /// <summary>An item of a tree.</summary>
    TreeItem,

    /// <summary>An SVG graphic that is a document in itself, with parts a reader can step through.</summary>
    GraphicsDocument,
}
