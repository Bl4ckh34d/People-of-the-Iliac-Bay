using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class Program
{
    static MethodDeclarationSyntax Method(string path,string name)=>CSharpSyntaxTree.ParseText(File.ReadAllText(path))
        .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText==name && value.Body!=null);

    static int Main(string[] args)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/NativeVendorTradeWindow.cs")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Source root missing.");
        string native=Path.Combine(root.Parent.FullName,"daggerfall-unity/Assets/Scripts/Game/UserInterfaceWindows");
        string source=Path.Combine(root.FullName,"mod/Scripts/NativeVendorTradeWindow.cs");
        var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(source)).GetRoot();
        var setup=tree.DescendantNodes().OfType<MethodDeclarationSyntax>().SingleOrDefault(value=>value.Identifier.ValueText=="Setup");
        string setupMethod=setup==null?string.Empty:setup.ToFullString();
        var update=Method(Path.Combine(native,"DaggerfallBaseWindow.cs"),"Update");
        var setupBranch=update.Body.Statements.OfType<IfStatementSyntax>().Single(value=>value.Condition.ToString()=="!isSetup");
        string gameSource=Path.Combine(native,"../GameManager.cs");
        string mouseSource=Path.Combine(native,"../PlayerMouseLook.cs");
        var mouseStatements=Method(mouseSource,"Update").Body.Statements;
        int mouseStop=mouseStatements.IndexOf(mouseStatements.OfType<IfStatementSyntax>().Single(value=>value.Condition.ToString()=="!enableMouseLook"));
        string mousePrefix=string.Concat(mouseStatements.Take(mouseStop+1).Select(value=>value.ToFullString()));
        string managerSource=Path.Combine(native,"../UserInterface/UserInterfaceManager.cs");
        var adapterPush=Method(source,"OnPush");
        // Diagnostic mode restores only the pre-fix unsafe tab selection in memory.
        if(args.Contains("--pre-fix"))
        {
            var guard=adapterPush.Body.Statements.OfType<IfStatementSyntax>().Single(value=>value.Condition.ToString()=="IsSetup");
            adapterPush=adapterPush.ReplaceNode(guard,guard.Statement);
        }
        string fixture="""
using System;
using System.Collections.Generic;
using System.Reflection;
using PlayerMouseLook=MouseLook;
public enum TabPages {WeaponsAndArmor,MagicItems,ClothingAndMisc,Ingredients}
public enum ItemGroups {Weapons,Clothing}
public enum RemoteTargetTypes {Merchant}
public sealed class Item {public ItemGroups ItemGroup=ItemGroups.Clothing;}
public sealed class ItemCollection {public int Count=1;public Item Item=new Item();public Item GetItem(int index)=>Item;}
public sealed class Button {public object BackgroundTexture;}
public sealed class Label {public void SetText(object value){}}
public sealed class Scroller {public object Items;public void ResetScroll(){}}
public static class TextFile {public sealed class Token {}}
public sealed class Player {public ItemCollection Items=new ItemCollection();}
public sealed class PlayerGPS {public struct DiscoveredBuilding {public int quality;}}
public sealed class EnterExit {public bool IsPlayerInsideBuilding;}
public sealed class Racial {public bool Suppress;public bool GetSuppressInventory(out string message){message="suppressed";return Suppress;}}
public sealed class Effects {public Racial Racial;public Racial GetRacialOverrideEffect()=>Racial;}
public enum IMECompositionMode {Auto,On}
public enum CursorLockMode {None,Locked}
public enum KeyCode {Escape}
public static class Cursor {public static CursorLockMode lockState;}
public static class Input {public static IMECompositionMode imeCompositionMode;public static bool GetKeyDown(KeyCode key)=>false;public static bool GetMouseButtonDown(int button)=>false;}
public static class Time {public static float timeScale=1,realtimeSinceStartup=5;}
public sealed class InputManager {public static InputManager Instance=new InputManager();public bool IsPaused,CursorVisible;public enum Actions {ActivateCursor}public bool ActionStarted(Actions action)=>false;public bool AnyKeyDownIgnoreAxisBinds;public bool GetBackButtonDown()=>false;}
public sealed class Activate {public void SetClickDelay(){}}
public sealed class GameManager {
  public static GameManager Instance=new GameManager();public static bool HasInstance=>true;
  public static bool IsGamePaused=>Instance.isGamePaused;
  public EnterExit PlayerEnterExit=new EnterExit();public Effects PlayerEffectManager=new Effects();public Activate PlayerActivate=new Activate();
  public MouseLook PlayerMouseLook;
  bool isGamePaused,hudDisabledByPause;float savedTimeScale;
  NATIVE_PAUSE
}
public sealed class DaggerfallUI {public static DaggerfallUI Instance=new DaggerfallUI();public bool ShowVersionText,enableHUD=true;public float timeClosedInputMessageBox;public UserInterfaceWindows.DaggerfallHUD DaggerfallHUD=new UserInterfaceWindows.DaggerfallHUD();public static void MessageBox(string message){}}
public interface IUserInterfaceWindow {bool PauseWhileOpen{get;set;}void OnPush();void OnPop();void OnReturn();}
namespace UserInterfaceWindows {public sealed class DaggerfallHUD:DaggerfallTradeWindow{public bool Enabled;}}
public sealed class WindowManager {
  Stack<IUserInterfaceWindow> windows=new Stack<IUserInterfaceWindow>();public IUserInterfaceWindow TopWindow=>windows.Count>0?windows.Peek():null;
  public WindowManager(){windows.Push(DaggerfallUI.Instance.DaggerfallHUD);}
  NATIVE_ADD
  NATIVE_REMOVE
  public void Push(IUserInterfaceWindow window){AddWindow(window);}public void Pop(){RemoveWindow();}
}
public sealed class MouseLook {
  bool forceHideCursor,cursorActive,simpleCursorLock;bool lockCursor=true;public bool enableMouseLook=true;
  public void Tick(){NATIVE_MOUSE_PREFIX}
  NATIVE_FORCE_HIDE
}
public sealed class MovieFile {public bool EndOfFile;}
public sealed class Video {public bool Playing=true;public MovieFile VidFile=new MovieFile();public void Dispose(){}}
public sealed class CustomVideo {public bool IsPlaying;public void Stop(){}public void Dispose(){}}
public sealed class VideoFixture:DaggerfallTradeWindow {
  Video video=new Video();CustomVideo customVideo;bool useCustomVideo,hideCursor=true,endOnAnyKey=true;
  public int Finished,Ended;
  public void StartVideo(){DoHideCursor(hideCursor);}
  public void FinishNaturally(){video.VidFile.EndOfFile=true;Update();}
  void RaiseOnVideoFinishedHandler(){Finished++;}void RaiseOnVideoEndGlobalEvent(){Ended++;}
  NATIVE_VIDEO_HIDE
  NATIVE_VIDEO_UPDATE
  NATIVE_VIDEO_POP
}
public static class NativeVendorTradeAdapter {public static int Activations;public static void SetActive(object window,string person){Activations++;}}
public class DaggerfallTradeWindow:IUserInterfaceWindow {
  private PlayerGPS.DiscoveredBuilding buildingDiscoveryData;
  protected bool isSetup; public bool IsSetup=>isSetup;public int SetupCalls,BasePushCalls,RefreshCalls;public bool Closed;
  protected Player PlayerEntity=new Player();protected ItemCollection localItems,remoteItems;protected RemoteTargetTypes remoteTargetType;
  protected TabPages selectedTabPage;protected Button weaponsAndArmorButton,magicItemsButton,clothingAndMiscButton,ingredientsButton;
  protected object weaponsAndArmorSelected,weaponsAndArmorNotSelected,magicItemsSelected,magicItemsNotSelected,clothingAndMiscSelected,clothingAndMiscNotSelected,ingredientsSelected,ingredientsNotSelected;
  protected Label itemInfoPanelLabel;protected Scroller localItemListScroller;protected object localItemsFiltered;
  public ItemCollection MerchantItems=new ItemCollection();
  public bool PauseWhileOpen{get;set;}=true;
  protected virtual void Setup(){SetupCalls++;weaponsAndArmorButton=new Button();magicItemsButton=new Button();clothingAndMiscButton=new Button();ingredientsButton=new Button();localItemListScroller=new Scroller();SelectTabPage(TabPages.WeaponsAndArmor);}
  public virtual void OnPush(){BasePushCalls++;localItems=PlayerEntity.Items;remoteItems=MerchantItems;Refresh();}
  public virtual void OnPop(){}public void OnReturn(){}
  public virtual void Update(){}
  public void Tick(){SETUP_BRANCH}
  public void Refresh(){if(IsSetup)RefreshCalls++;}
  protected void FilterLocalItems(){}
  protected void CloseWindow(){Closed=true;}
  NATIVE_SELECT
  public TabPages Selected=>selectedTabPage;
  public bool Bound=>ReferenceEquals(localItems,PlayerEntity.Items) && ReferenceEquals(remoteItems,MerchantItems);
  public int Quality=>buildingDiscoveryData.quality;
}
public sealed class Fixture:DaggerfallTradeWindow {
  static readonly FieldInfo nativeBuilding=typeof(DaggerfallTradeWindow).GetField("buildingDiscoveryData",BindingFlags.Instance|BindingFlags.NonPublic);
  readonly string personId="actual-vendor";readonly ItemCollection originals=new ItemCollection();
  ADAPTER_PUSH
  ADAPTER_SETUP
  public static int Run(){
    int passed=0,total=0;
    void Check(string name,Action test){total++;try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){Console.WriteLine("FAIL "+name+": "+error.GetType().Name+" "+error.Message);}}
    void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    GameManager.Instance=new GameManager();
    Check("new market push binds stock before UI setup",()=>{var window=new Fixture();window.OnPush();Need(window.Bound && window.Quality==10 && !window.IsSetup,"uninitialized market bindings");});
    Check("first native update selects clothing stock after controls exist",()=>{var window=new Fixture();window.OnPush();window.Tick();Need(window.IsSetup && window.SetupCalls==1 && window.Selected==TabPages.ClothingAndMisc,"initial stock tab missing");});
    Check("weapons stock selects native weapons tab",()=>{var window=new Fixture();window.originals.Item.ItemGroup=ItemGroups.Weapons;window.OnPush();window.Tick();Need(window.Selected==TabPages.WeaponsAndArmor,"wrong weapons tab");});
    Check("re-push uses existing native controls",()=>{var window=new Fixture();window.OnPush();window.Tick();window.originals.Item.ItemGroup=ItemGroups.Weapons;window.OnPush();window.Tick();Need(window.SetupCalls==1 && window.Selected==TabPages.WeaponsAndArmor && window.RefreshCalls==1,"re-push did not refresh existing window");});
    Check("empty stock uses miscellaneous tab",()=>{var window=new Fixture();window.originals.Count=0;window.OnPush();window.Tick();Need(window.Selected==TabPages.ClothingAndMisc,"wrong empty-stock tab");});
    Check("suppressed market closes before UI access",()=>{GameManager.Instance.PlayerEffectManager.Racial=new Racial{Suppress=true};var window=new Fixture();window.OnPush();Need(window.Closed && window.SetupCalls==0,"suppression failed");GameManager.Instance.PlayerEffectManager.Racial=null;});
    Check("indoor push keeps native trade path",()=>{GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding=true;var window=new Fixture();window.OnPush();window.Tick();Need(window.BasePushCalls==1 && window.Bound && window.SetupCalls==1,"indoor native push missing");GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding=false;});
    void ResetInput(){GameManager.Instance=new GameManager();InputManager.Instance=new InputManager();Time.timeScale=1;}
    void UiOwnsInput(MouseLook mouse){mouse.Tick();mouse.Tick();Need(GameManager.IsGamePaused && InputManager.Instance.IsPaused && Time.timeScale==0 && !mouse.enableMouseLook && InputManager.Instance.CursorVisible && Cursor.lockState==CursorLockMode.None,"native pause/cursor ownership missing");}
    void HudOwnsInput(WindowManager manager,MouseLook mouse){manager.Pop();mouse.Tick();mouse.Tick();Need(!GameManager.IsGamePaused && !InputManager.Instance.IsPaused && Time.timeScale==1 && mouse.enableMouseLook && !InputManager.Instance.CursorVisible && Cursor.lockState==CursorLockMode.Locked,"native HUD input not restored");}
    Check("native stack pauses new trade and releases native cursor",()=>{ResetInput();var manager=new WindowManager();var mouse=new MouseLook();var window=new Fixture();manager.Push(window);UiOwnsInput(mouse);Need(ReferenceEquals(manager.TopWindow,window),"trade missing from stack");});
    Check("cancel returns pause and cursor to HUD",()=>{ResetInput();var manager=new WindowManager();var mouse=new MouseLook();manager.Push(new Fixture());UiOwnsInput(mouse);HudOwnsInput(manager,mouse);});
    Check("confirmation return retains trade pause before final close",()=>{ResetInput();var manager=new WindowManager();var mouse=new MouseLook();var window=new Fixture();manager.Push(window);manager.Push(new DaggerfallTradeWindow());manager.Pop();UiOwnsInput(mouse);Need(ReferenceEquals(manager.TopWindow,window),"confirmation lost trade");HudOwnsInput(manager,mouse);});
    Check("re-push reacquires native pause and cursor",()=>{ResetInput();var manager=new WindowManager();var mouse=new MouseLook();var window=new Fixture();manager.Push(window);window.Tick();HudOwnsInput(manager,mouse);manager.Push(window);UiOwnsInput(mouse);HudOwnsInput(manager,mouse);});
    Check("forced native video pop leaves force-hide ownership orphaned",()=>{ResetInput();var manager=new WindowManager();var mouse=new MouseLook();GameManager.Instance.PlayerMouseLook=mouse;var video=new VideoFixture();manager.Push(video);video.StartVideo();manager.Pop();manager.Push(new Fixture());mouse.Tick();mouse.Tick();Need(GameManager.IsGamePaused && InputManager.Instance.IsPaused && Time.timeScale==0 && !InputManager.Instance.CursorVisible && Cursor.lockState==CursorLockMode.Locked && video.Ended==0,"forced video pop did not reproduce orphaned cursor ownership");});
    Check("regular native video completion releases cursor before trade",()=>{ResetInput();var manager=new WindowManager();var mouse=new MouseLook();GameManager.Instance.PlayerMouseLook=mouse;var video=new VideoFixture();manager.Push(video);video.StartVideo();video.FinishNaturally();Need(video.Closed && video.Ended==1 && video.Finished==1,"native video completion path did not run");manager.Pop();manager.Push(new Fixture());UiOwnsInput(mouse);});
    Console.WriteLine(passed+"/"+total+" passed");return passed==total?0:1;
  }
}
""".Replace("SETUP_BRANCH",setupBranch.ToFullString())
            .Replace("NATIVE_SELECT",Method(Path.Combine(native,"DaggerfallInventoryWindow.cs"),"SelectTabPage").ToFullString())
            .Replace("ADAPTER_PUSH",adapterPush.ToFullString()).Replace("ADAPTER_SETUP",setupMethod)
            .Replace("NATIVE_PAUSE",Method(gameSource,"PauseGame").ToFullString())
            .Replace("NATIVE_ADD",Method(managerSource,"AddWindow").ToFullString())
            .Replace("NATIVE_REMOVE",Method(managerSource,"RemoveWindow").ToFullString())
            .Replace("NATIVE_MOUSE_PREFIX",mousePrefix)
            .Replace("NATIVE_FORCE_HIDE",Method(mouseSource,"ForceHideCursor").ToFullString())
            .Replace("NATIVE_VIDEO_HIDE",Method(Path.Combine(native,"DaggerfallVidPlayerWindow.cs"),"DoHideCursor").ToFullString())
            .Replace("NATIVE_VIDEO_UPDATE",Method(Path.Combine(native,"DaggerfallVidPlayerWindow.cs"),"Update").ToFullString())
            .Replace("NATIVE_VIDEO_POP",Method(Path.Combine(native,"../UserInterface/UserInterfaceWindow.cs"),"OnPop").ToFullString().Replace("public virtual","public override"));
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path));
        var compilation=CSharpCompilation.Create("MarketTradeLifecycleFixture",new[]{CSharpSyntaxTree.ParseText(fixture)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
