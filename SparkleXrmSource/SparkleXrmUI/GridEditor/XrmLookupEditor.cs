// DataEditor.cs
//

using jQueryApi;
using jQueryApi.UI;
using jQueryApi.UI.Widgets;
using Slick;
using SparkleXrm.CustomBinding;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Xrm;
using Xrm.Sdk;
using Xrm.Sdk.Metadata;


namespace SparkleXrm.GridEditor
{
    public class XrmLookupEditorOptions
    {
        public Action<string, Action<EntityCollection>> queryCommand; // searchTerm, queryCallBack
        public string nameAttribute;
        public string idAttribute;
        public string typeCodeAttribute;
        public string[] columns;
        public bool showImage = true;
        public bool showFooter = false;
        public XrmLookupEditorButton footerButton = null;
        public bool useQuickCreate = false;
       
    }
    public class XrmLookupEditorButton
    {
        public string Label = "";
        public string Tooltip = "";
        public Action<object> OnClick;
        public string Image = null;
    }
    public class XrmLookupEditor : GridEditorBase
    {
        public static EditorFactory LookupEditor;

        static XrmLookupEditor()
        {
            LookupEditor = delegate(EditorArguments args)
            {
                XrmLookupEditor editor = new XrmLookupEditor(args);
                return editor;
            };

        }
       
        // Formatter to show a lookup with hyperlink
        public static string Formatter(int row, int cell, object value, Column columnDef, object dataContext)
        {
            if (value != null)
            {
                //TOOO: Add an onlclick handler to the grid to handle the lookup links
                EntityReference entityRef = (EntityReference)value;
                return "<a href='#' class='sparkle-lookup-link' entityid='" + entityRef.Id +"' typename='" + entityRef.LogicalName + "'>" + XmlHelper.Encode(entityRef.Name) +"</a>";
            }
            else
                return "";
        }


        private jQueryObject _input;
        private jQueryObject _container;
        private AutoCompleteObject _autoComplete;
        private bool _searchOpen = false;
        private EntityReference _value = new EntityReference(null, null, String.Empty);
        private EntityReference _originalValue = new EntityReference(null, null, String.Empty);
        private int totalRecordsReturned;

        public XrmLookupEditor(EditorArguments args) : base(args)
        {
            XrmLookupEditor self = this;

            _args = args;
            _container = jQuery.FromHtml("<div><table class='inline-edit-container' cellspacing='0' cellpadding='0'><tr><td><INPUT type=text class='sparkle-input-inline' /></td><td class='lookup-button-td'><input type=button class='sparkle-lookup-button' /></td></tr></table></div>");
            _container.AppendTo(_args.Container);

            jQueryObject inputField = _container.Find(".sparkle-input-inline");
            jQueryObject selectButton = _container.Find(".sparkle-lookup-button");
            _input = inputField;
            _input.Focus().Select();
               
            _autoComplete = inputField.Plugin<AutoCompleteObject>();

            AutoCompleteOptions options = new AutoCompleteOptions();
            options.Position = new Dictionary<string, object>("collision", "fit");
            options.MinLength = 100000;
            options.Delay = 0; // TODO- set to something that makes sense
            XrmLookupEditorOptions editorOptions = (XrmLookupEditorOptions)args.Column.Options; 

            bool justSelected = false;
            options.Select = delegate(jQueryEvent e, AutoCompleteSelectEvent uiEvent)
            {
                if (_value == null) _value = new EntityReference(null,null,null);

                // Note we assume that the binding has added an array of string items
                AutoCompleteItem item = (AutoCompleteItem)uiEvent.Item;
                EntityReference itemRef = (EntityReference)item.Value;
                if (itemRef.LogicalName == "footerlink")
                {
                    XrmLookupEditorButton button = editorOptions.footerButton;
                    button.OnClick(item);
                }
                else
                {
                    string value = item.Label;
                    _input.Value(value);
                    _value.Id = itemRef.Id;
                    _value.Name = itemRef.Name;
                    _value.LogicalName = ((EntityReference)item.Value).LogicalName;
                    justSelected = true;
                }
                Script.Literal("return false;");

            };
            
            options.Focus = delegate(jQueryEvent e, AutoCompleteFocusEvent uiEvent)
            {
                // Prevent the value being updated in the text box as we scroll through the results
                Script.Literal("return false;");
            };

            options.Open = delegate(jQueryEvent e, jQueryObject o)
            {
                self._searchOpen = true;
                if (editorOptions.showFooter && totalRecordsReturned>0)
                {
                    WidgetObject menu = (WidgetObject)Script.Literal("{0}.autocomplete({1})", _input, "widget");
                    AddFooter(menu,totalRecordsReturned);
                }
            };

            options.Close = delegate(jQueryEvent e, jQueryObject o)
            {
                self._searchOpen = false;
                WidgetObject menu = (WidgetObject)Script.Literal("{0}.autocomplete({1})", _input, "widget");
                jQueryObject footer = menu.Next();
                if (footer.Length > 0 || footer.HasClass("sparkle-menu-footer"))
                {
                    footer.Hide();
                }
            };

            // If there multiple names, add them to the columnAttributes
            string[] columns = editorOptions.nameAttribute.Split(",");

            if (columns.Length > 1)
            {
                editorOptions.columns = columns;
                editorOptions.nameAttribute = columns[0];
            }

            // wire up source to CRM search
            Action<AutoCompleteRequest, Action<AutoCompleteItem[]>> queryDelegate = delegate(AutoCompleteRequest request, Action<AutoCompleteItem[]> response)
            {
                 // Get the option set values
                editorOptions.queryCommand(request.Term, delegate(EntityCollection fetchResult)
                {
                    if (fetchResult.TotalRecordCount > fetchResult.Entities.Count)
                    {
                        totalRecordsReturned = fetchResult.TotalRecordCount;
                    }
                    else
                    {
                        totalRecordsReturned = fetchResult.Entities.Count;
                    }
                  
                    int recordsFound = fetchResult.Entities.Count;
                    bool noRecordsFound = recordsFound == 0;
                    XrmLookupEditorButton button = editorOptions.footerButton;
                    bool footerButton = (button != null);

                    AutoCompleteItem[] results = new AutoCompleteItem[recordsFound + (footerButton ? 1 : 0) + (noRecordsFound ? 1 :0) ];

                    for (int i = 0; i < recordsFound; i++)
                    {
                        results[i] = new AutoCompleteItem();
                        results[i].Label = (string)fetchResult.Entities[i].GetAttributeValue(editorOptions.nameAttribute);
                        EntityReference id = new EntityReference(null, null, null);
                        id.Name = results[i].Label;
                        id.LogicalName = fetchResult.Entities[i].LogicalName;
                        id.Id = (Guid)fetchResult.Entities[i].GetAttributeValue(editorOptions.idAttribute);
                        results[i].Value = id;
                        XrmLookupBinding.GetExtraColumns(editorOptions.columns, fetchResult, results, i);
                        string typeCodeName = fetchResult.Entities[i].LogicalName;
                        
                        // Get the type code from the name to find the icon
                        if (!string.IsNullOrEmpty(editorOptions.typeCodeAttribute))
                        {
                            typeCodeName = fetchResult.Entities[i].GetAttributeValue(editorOptions.typeCodeAttribute).ToString();
                        }

                        if (editorOptions.showImage)
                        {
                            results[i].Image = MetadataCache.GetSmallIconUrl(typeCodeName);
                        }
                    }

                    int itemsCount = recordsFound;
                    if (noRecordsFound)
                    {
                        AutoCompleteItem noRecordsItem = new AutoCompleteItem();
                        noRecordsItem.Label = SparkleResourceStrings.NoRecordsFound;
                        results[itemsCount] = noRecordsItem;                 
                        itemsCount++;
                    }

                    if (footerButton)
                    {
                        // Add the add new
                        AutoCompleteItem addNewLink = new AutoCompleteItem();
                        addNewLink.Label = button.Label;
                        addNewLink.Image = button.Image;
                        addNewLink.ColumnValues = null;
                        addNewLink.Value = new Entity("footerlink");
                        results[itemsCount] = addNewLink;
                    }
                    response(results);

                    // Disable it now so typing doesn't trigger a search
                    AutoCompleteOptions disableOption = new AutoCompleteOptions();
                    disableOption.MinLength = 100000;
                    _autoComplete.AutoComplete(disableOption);
                });
                
            };

            options.Source = queryDelegate;
            inputField = _autoComplete.AutoComplete(options);
            RenderItemDelegate autoCompleteDelegates = ((RenderItemDelegate)Script.Literal("{0}.data('ui-autocomplete')", inputField));
            autoCompleteDelegates._renderItem = delegate(object ul, AutoCompleteItem item)
            {
                if(item.Value==item.Label)
                {
                    return (object)jQuery.Select("<li class='ui-state-disabled'>"+item.Label+"</li>").AppendTo((jQueryObject)ul);
                }

                string itemHtml = "<a class='sparkle-menu-item'>";
                // Allow for no image by passing false to 'ShowImage' on the XrmLookupEditorOptions options
                if (item.Image != null)
                {
                    itemHtml += "<span class='sparkle-menu-item-img'><img src='" + item.Image + "'/></span>";
                }
                itemHtml += "<span class='sparkle-menu-item-label'>" + item.Label + "</span><br/>";
                if (item.ColumnValues != null && item.ColumnValues.Length > 0)
                {
                    foreach (string value in item.ColumnValues)
                    {
                        itemHtml += "<span class='sparkle-menu-item-moreinfo'>" + value + "</span>";
                    }
                }
                itemHtml += "</a>";
                return (object)jQuery.Select("<li>").Append(itemHtml).AppendTo((jQueryObject)ul);
            };
   
            // Add the click binding to show the drop down
            selectButton.Click(delegate(jQueryEvent e)
            {
                AutoCompleteOptions enableOption = new AutoCompleteOptions();
                enableOption.MinLength = 0;
                _autoComplete.AutoComplete(enableOption);
                _autoComplete.AutoComplete(AutoCompleteMethod.Search, inputField.GetValue());
                
            });

            // Bind return to searching 
            _input.Keydown(delegate(jQueryEvent e)
            {
                if (e.Which == 13 && !justSelected) // Return pressed - but we want to do a search not move to the next cell
                {
                    if (inputField.GetValue().Length > 0)
                        selectButton.Click();
                    else
                    {
                        // Set value to null
                        _value = null;
                        return;
                    }
                }
                else if (e.Which == 13)
                {
                    return;
                }
                if (self._searchOpen)
                {
                    switch (e.Which)
                    {
                        case 9:
                        case 13: // Return
                        case 38: // Up - don't navigate - but use the dropdown to select search results
                        case 40: // Down - don't navigate - but use the dropdown to select search results
                            e.PreventDefault();
                            e.StopPropagation();
                            break;
                    }
                }
                else
                {
                    switch (e.Which)
                    {
                        case 13: // Return
                            e.PreventDefault();
                            e.StopPropagation();
                            break;

                    }
                }
                justSelected = false;
            });

        }

        public static void AddFooter(WidgetObject menu, int recordCount)
        {
            jQueryObject footer = menu.Next();
            if (footer.Length == 0 || !footer.HasClass("sparkle-menu-footer"))
            {
                footer = jQuery.FromHtml("<div class='sparkle-menu-footer ui-front'></div>");
                menu.Parent().Append(footer);
            }

            if (footer != null)
            {
                footer.Html("");
                jQueryObject footerContent = jQuery.FromHtml("<span class='sparkle-menu-footer-content'></span>");
                jQueryObject footerLeft = jQuery.FromHtml("<span class='sparkle-menu-footer-left'></span>");
                jQueryObject footerRight = jQuery.FromHtml("<span class='sparkle-menu-footer-right'></span>");
                footerContent.Append(footerLeft);
                footerContent.Append(footerRight);
                footerLeft.Append(String.Format(SparkleResourceStrings.LookupFooter, recordCount));
                footer.Append(footerContent);
            }

            jQueryPosition pos = menu.Position();
            int height = menu.GetHeight();
            int width = menu.GetWidth();
            if (footer != null && footer.Length > 0)
            {
                footer.Show();
                footer.CSS("top", (pos.Top + height + 4).ToString() + "px");
                footer.CSS("left", (pos.Left).ToString() + "px");
                footer.Width(width);
            }
        }

        public override void Destroy()
        {
            _input.Plugin<AutoCompleteObject>().AutoComplete(AutoCompleteMethod.Close);
            _input.Plugin<AutoCompleteObject>().AutoComplete(AutoCompleteMethod.Destroy);
            _container.Remove();
            _autoComplete.Remove();
            _autoComplete = null;
        }

        public override void Show()
        {        
        }

        public override void Hide()
        {                
        }

        public override void Position(jQueryPosition position)
        {       
        }

        public override void Focus()
        {
            _input.Focus();
        }

        public override void LoadValue(Dictionary<string, object> item)
        {
            _originalValue = (EntityReference)item[_args.Column.Field];
            if (_originalValue != null)
            {
                _value = new EntityReference(_originalValue.Id, _originalValue.LogicalName, _originalValue.Name);
                _input.Value(_originalValue.Name);
            }
        }

        public override object SerializeValue()
        {
            // Ensure that the value is returned as null if the id is null
            if (_value!=null && _value.Id == null)
                return null;
            else
                return _value;
        }

        public override void ApplyValue(Dictionary<string, object> item, object state)
        {
            item[_args.Column.Field] = state;
            this.RaiseOnChange(item);
        }

        public override bool IsValueChanged()
        {
            if (_originalValue != null && _value != null)
            {
                string lvalue = _originalValue.Id!=null ? _originalValue.Id.ToString() : "";
                string rvalue = _value.Id!=null ? _value.Id.ToString() : "";
                return lvalue != rvalue;
            }
            else
            {
                return ((_originalValue!=null) || (_value!=null));
            }
        }

        public static Column BindColumn(Column column, Action<string, Action<EntityCollection>> queryCommand, string idAttribute, string nameAttribute, string typeCodeAttribute)
        {
            column.Editor = LookupEditor;
            XrmLookupEditorOptions currencyLookupOptions = new XrmLookupEditorOptions();
            currencyLookupOptions.queryCommand = queryCommand;
            currencyLookupOptions.idAttribute = idAttribute;
            currencyLookupOptions.nameAttribute = nameAttribute;
            currencyLookupOptions.typeCodeAttribute = typeCodeAttribute;
            column.Options = currencyLookupOptions;
            column.Formatter = XrmLookupEditor.Formatter;
            return column;
        }

        public static Column BindReadOnlyColumn(Column column,string typeCodeAttribute)
        {
            XrmLookupEditorOptions currencyLookupOptions = new XrmLookupEditorOptions();
            currencyLookupOptions.typeCodeAttribute = typeCodeAttribute;
            column.Options = currencyLookupOptions;
            column.Formatter = XrmLookupEditor.Formatter;

            return column;
        }
    }

    [Imported]
    public class RenderItemDelegate : jQueryObject
    {
        public Func<object, AutoCompleteItem, object> _renderItem;
        public Func<object, List<AutoCompleteItem>, object> _renderMenu;
        public Action _resizeMenu;
    }
    [Imported]
    [IgnoreNamespace]
    [ScriptName("Object")]
    public class AutoCompleteItem
    {
        public string Label;
        public object Value;
        public string Image;
        public object Data;
        public string[] ColumnValues;
    }
    [Imported]
    [IgnoreNamespace]
    [ScriptName("Object")]
    public class AutoCompleteRequest
    {
        public string Term;
       
    }
   
}
