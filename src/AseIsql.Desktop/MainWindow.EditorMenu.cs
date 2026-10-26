using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // Right-click in the Query editor: edit and run commands, plus object actions for the identifier selected or
    // under the pointer. Opening the menu reads metadata only; SQL actions require an explicit choice.
    private void AttachEditorMenu()
    {
        var menu=new ContextMenu();
        Editor.TextArea.ContextMenu=menu;
        Editor.TextArea.PreviewMouseRightButtonDown+=(_,e)=> {
            var position=Editor.GetPositionFromPoint(e.GetPosition(Editor));
            if(position==null)return;
            int offset=Editor.Document.GetOffset(position.Value.Location);
            // A click outside the selection moves the caret there, like other editors; inside it keeps the selection.
            if(Editor.SelectionLength==0||offset<Editor.SelectionStart||offset>Editor.SelectionStart+Editor.SelectionLength){Editor.Select(offset,0);Editor.TextArea.Caret.Offset=offset;}
            Editor.TextArea.Focus();
        };
        menu.Opened+=async (_,_)=> { BuildEditorMenu(menu); await ResolveEditorObjectMenu(menu); };
        menu.Closed+=(_,_)=>CloseEditorObjectMenu(menu);
        SessionTabs.SelectionChanged+=(_,e)=> { if(e.Source==SessionTabs){menu.IsOpen=false;CloseEditorObjectMenu(menu);} };
        Closed+=(_,_)=>CloseEditorObjectMenu(menu);
    }
    private void BuildEditorMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        BeginEditorObjectMenu(menu);
        bool selected=Editor.SelectionLength>0;
        void Add(ItemsControl parent,string header,string? gesture,Action action,bool enabled=true){var item=new MenuItem {Header=header,InputGestureText=gesture??"",IsEnabled=enabled};item.Click+=(_,_)=>action();parent.Items.Add(item);}
        AddEditorObjectPlaceholder(menu,editorObjectRequest?.Target);
        Add(menu,"Execute selection","Ctrl+E",()=>_=ExecuteAsync(selectionOnly:true),selected&&running==null);
        Add(menu,"Execute current line","Alt+F9",()=>_=ExecuteAsync(lineOnly:true),running==null);
        Add(menu,"Execute query","F9",()=>_=ExecuteAsync(),running==null);
        Add(menu,"Fill parameters…","Ctrl+Shift+P",()=>FillParametersClick(this,new RoutedEventArgs()),SqlParameters.Names(selected?Editor.SelectedText:Editor.Text).Count>0);
        menu.Items.Add(new Separator());
        Add(menu,"Cut","Ctrl+X",()=>ApplicationCommands.Cut.Execute(null,Editor.TextArea),selected);
        Add(menu,"Copy","Ctrl+C",()=>ApplicationCommands.Copy.Execute(null,Editor.TextArea),selected);
        Add(menu,"Paste","Ctrl+V",()=>ApplicationCommands.Paste.Execute(null,Editor.TextArea),ApplicationCommands.Paste.CanExecute(null,Editor.TextArea));
        Add(menu,"Select all","Ctrl+A",()=>Editor.SelectAll());
        menu.Items.Add(new Separator());
        Add(menu,"Comment / uncomment",null,()=>CommentClick(this,new RoutedEventArgs()));
        Add(menu,"Selection to UPPERCASE",null,()=>UpperClick(this,new RoutedEventArgs()),selected);
        Add(menu,"Selection to lowercase",null,()=>LowerClick(this,new RoutedEventArgs()),selected);
        Add(menu,"Find…","Ctrl+F",()=>FindClick(this,new RoutedEventArgs()));
    }
    // The identifier around the caret, including owner/database dots and [bracketed] parts.
    private string IdentifierAtCaret()
    {
        var document=Editor.Document;int caret=Editor.TextArea.Caret.Offset,start=caret,end=caret;
        static bool Part(char c)=>char.IsLetterOrDigit(c)||c is '_' or '#' or '@' or '$' or '.' or '[' or ']';
        while(start>0&&Part(document.GetCharAt(start-1)))start--;
        while(end<document.TextLength&&Part(document.GetCharAt(end)))end++;
        return document.GetText(start,end-start).Trim('.');
    }
}
