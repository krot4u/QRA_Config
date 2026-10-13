using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QraConfig.Models;

namespace QraConfig.Desktop;

public partial class MainWindow
{
    private Window DialogWindow(string title, FrameworkElement body, out StackPanel actions)
    {
        var window = new Window { Title=title, Owner=this, Width=640, MinWidth=520, MaxHeight=SystemParameters.WorkArea.Height-60, SizeToContent=SizeToContent.Height, ResizeMode=ResizeMode.NoResize, WindowStartupLocation=WindowStartupLocation.CenterOwner, ShowInTaskbar=false };
        window.SourceInitialized += (_,_) => DarkTitle(window);
        var grid=new Grid {Margin=new Thickness(24)}; grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var heading=Text(title,22);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,18);grid.Children.Add(heading);
        var scroll=new ScrollViewer {Content=body,MaxHeight=Math.Max(200,SystemParameters.WorkArea.Height-260),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);grid.Children.Add(scroll);
        actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,24,0,0)};Grid.SetRow(actions,2);grid.Children.Add(actions);window.Content=grid;return window;
    }
    private bool Confirm(string title, string description, string actionText)
    {
        var body=Text(description);body.LineHeight=23;
        var window=DialogWindow(title,body,out var actions);
        var cancel=new Button {Content="Отмена",IsCancel=true,IsDefault=true,MinWidth=108,Margin=new Thickness(0,0,10,0)};
        var accept=new Button {Content=actionText,Style=(Style)FindResource("PrimaryButton"),MinWidth=130};
        accept.Click+=(_,_)=>window.DialogResult=true;actions.Children.Add(cancel);actions.Children.Add(accept);
        return window.ShowDialog()==true;
    }
    private void ShowInformation(string title,string description)
    {
        var body=Text(description);body.LineHeight=23;
        var window=DialogWindow(title,body,out var actions);actions.Children.Add(new Button {Content="Понятно",IsCancel=true,IsDefault=true,MinWidth=120});window.ShowDialog();
    }
    private string BuildPreview(JsonObject before,JsonObject after)
    {
        var lines=new List<string>();
        foreach(var key in before.Select(x=>x.Key).Union(after.Select(x=>x.Key)))
        {
            if(JsonNode.DeepEquals(before[key],after[key]))continue;
            if(key=="frnServers"){lines.Add("Список серверов: изменён");continue;}
            var leaf=key[(key.LastIndexOf(':')+1)..];
            if(key.StartsWith("pocAvatar:")){lines.Add("Личное фото PoC: "+(after.ContainsKey(key)?"выбрано новое":"удалено"));continue;}
            if(key.StartsWith("profileAudio:")){lines.Add("Звук профиля: параметры изменены");continue;}
            if(key.StartsWith("dmrOptions:")){lines.Add("DMR: параметры изменены");continue;}
            if(leaf.Contains("Password",StringComparison.OrdinalIgnoreCase) || key.StartsWith("server:") || leaf=="userEmail"){lines.Add(Label(leaf)+": •••• → ••••");continue;}
            string Display(JsonNode? value)
            {
                if(value==null)return "отсутствует";if(value.GetValueKind()==JsonValueKind.True)return "включено";if(value.GetValueKind()==JsonValueKind.False)return "выключено";
                var text=value.GetValueKind()==JsonValueKind.String?value.GetValue<string>():value.ToJsonString();return text.Length>70?text[..67]+"…":text;
            }
            lines.Add(Label(Labels.ContainsKey(key)?key:leaf)+": "+Display(before[key])+" → "+Display(after[key]));
        }
        return string.Join("\n\n",lines)+"\n\nТелефон проверит конфликт с текущими настройками. Назначения физических кнопок сохранятся.";
    }
    private void EditProfile(ServerProfile? existing)
    {
        if(_draft==null)return;
        if(_invalid.Count>0){Status("Сначала исправьте отмеченные поля.","error");return;}
        var panel=new StackPanel();
        void Caption(string caption){var label=Text(caption,12,true);label.Margin=new Thickness(0,0,0,6);panel.Children.Add(label);}
        TextBox Field(string caption,string value){Caption(caption);var field=new TextBox{Text=value,Margin=new Thickness(0,0,0,14)};panel.Children.Add(field);return field;}
        var name=Field("Название",existing?.Name??"Новый сервер");
        var address=Field("Адрес сервера",existing?.Address??"");
        var port=Field("Порт",(existing?.Port??(_mode=="DMR"?62031:_mode=="P25"?41000:_mode=="POC"?443:10024)).ToString(CultureInfo.InvariantCulture));
        Caption("Кодировка");var charset=new ComboBox{ItemsSource=new[]{new Option("Кириллица (Windows-1251)","cp1251"),new Option("Западноевропейская (Windows-1252)","cp1252")},DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue=existing?.Charset??"cp1251",Margin=new Thickness(0,0,0,14)};panel.Children.Add(charset);
        Caption("Значок");
        var icons=new[]{"radio","cloud","globe","satellite","router","hub","headset","flag","wifi","dns","bolt","star"};
        var used=Profiles().Where(x=>x.Id!=existing?.Id).Select(x=>x.Icon).ToHashSet();
        var icon=new ComboBox{ItemsSource=Choices("serverIcon:profile",null),DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue=existing?.Icon??icons.FirstOrDefault(x=>!used.Contains(x))??"cloud",Margin=new Thickness(0,0,0,14)};panel.Children.Add(icon);
        var error=Text("",12,true);error.Foreground=ErrorColor;panel.Children.Add(error);
        var window=DialogWindow(existing==null?"Добавить сервер":"Параметры сервера",panel,out var actions);
        var cancel=new Button{Content="Отмена",IsCancel=true,Margin=new Thickness(0,0,10,0),MinWidth=100};
        var save=new Button{Content="Сохранить",IsDefault=true,Style=(Style)FindResource("PrimaryButton"),MinWidth=130};
        save.Click+=(_,_)=>{
            if(!int.TryParse(port.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var number)||number<1||number>65535){error.Text="Укажите порт 1–65535.";return;}
            try {
                var profile=new ServerProfile(name.Text.Trim(),address.Text.Trim(),number,_mode,Charset:(string)charset.SelectedValue,Icon:(string)icon.SelectedValue);
                ConfigModel.SaveProfile(_draft.Settings,profile,existing?.Id);EditorPolicy.InitializeProfile(_draft.Settings,profile);_profile=profile.Id;
                MarkEdited("selectedServer:"+_mode);window.DialogResult=true;
            }catch(InvalidOperationException ex){error.Text=ex.Message;}
        };
        actions.Children.Add(cancel);actions.Children.Add(save);window.ShowDialog();RenderEditor();
    }
    private void DeleteProfile(ServerProfile profile)
    {
        if(_draft==null)return;
        if(_invalid.Count>0){Status("Сначала исправьте отмеченные поля.","error");return;}
        if(!Confirm("Удалить профиль?",$"Профиль «{profile.Name}» и его настройки будут удалены из черновика. Изменение попадёт на телефон только после применения.","Удалить"))return;
        try {ConfigModel.DeleteProfile(_draft.Settings,profile.Id);_profile="";MarkEdited("selectedServer:"+profile.Protocol);RenderEditor();}
        catch(InvalidOperationException ex){Status(ex.Message,"error");}
    }
}
