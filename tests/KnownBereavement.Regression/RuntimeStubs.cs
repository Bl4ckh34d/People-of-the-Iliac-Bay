using System;
using System.Collections.Generic;
using FullSerializer;
using PeopleOfTheIliacBay.Core;

// Compile and execute the actual greeting adapter. These stand-ins do not prove
// Unity rendering, physical perception, or native save-file integration.
namespace DaggerfallWorkshop
{
    public sealed class DaggerfallUnity
    {
        public static readonly DaggerfallUnity Instance=new DaggerfallUnity();
        public readonly Clock WorldTime=new Clock();
        public sealed class Clock { public readonly Time Now=new Time(); }
        public sealed class Time { public double Seconds;public double ToSeconds(){return Seconds;} }
    }
}
namespace DaggerfallWorkshop.Game
{
    public sealed class DaggerfallUI
    {
        public static readonly DaggerfallUI Instance=new DaggerfallUI();
        public static readonly Manager UIManager=new Manager();
        public readonly object TalkWindow=new object();
        public sealed class Manager
        {
            private readonly Stack<object> windows=new Stack<object>();
            public object TopWindow {get{return windows.Count==0?null:windows.Peek();}}
            public bool ContainsWindow(object window){return windows.Contains(window);}
            public void PushWindow(object window){windows.Push(window);}
            public void PopWindow(){windows.Pop();}
        }
    }
}
namespace DaggerfallWorkshop.Game.UserInterfaceWindows
{
    public sealed class DaggerfallMessageBox
    {
        public bool ClickAnywhereToClose;
        public DaggerfallMessageBox(DaggerfallWorkshop.Game.DaggerfallUI.Manager manager){}
        public void SetText(string[] text){if(text.Length!=2)throw new Exception("Invalid greeting.");}
    }
}
namespace DaggerfallWorkshop.Game.Serialization
{
    public static class SaveLoadManager
    {
        public static fsData Serialize(Type type,object value)
        {fsData data;new fsSerializer().TrySerialize(type,value,out data).AssertSuccess();return data;}
        public static object Deserialize(Type type,fsData data)
        {object value=null;new fsSerializer().TryDeserialize(data,type,ref value).AssertSuccess();return value;}
    }
}
namespace UnityEngine
{
    public static class Debug { public static string LastMessage; public static void Log(string value){LastMessage=value;} }
}
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private WorldStateStore store;
        private bool saveFault=false;
        private static string ConversationArchiveRoot(){return System.IO.Path.Combine(System.IO.Path.GetTempPath(),"KnownBereavementRegression",Guid.NewGuid().ToString("N"));}
        public static void RunSourceProbe(WorldStateStore store,string person)
        {
            DaggerfallWorkshop.Game.DaggerfallUI.UIManager.PushWindow(DaggerfallWorkshop.Game.DaggerfallUI.Instance.TalkWindow);
            new PeopleOfTheIliacBayMod {store=store}.ProbeBereavementGreeting(person);
            DaggerfallWorkshop.Game.DaggerfallUI.UIManager.PopWindow();
        }
    }
}
