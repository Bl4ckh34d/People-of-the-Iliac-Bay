using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.DFU;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Reusable isolated scene: keeper heading and labelled floor arrow never rotate.
// Only the camera changes bearing. Atlas import and bearing selection are shared
// with the installed runtime, while native front frames are read from game data.
public sealed class KeeperReviewScene : EditorWindow
{
    private const string Folder="Assets/Game/Mods/PeopleOfTheIliacBay/PreviewGenerated";
    private Camera camera;
    private Transform flat;
    private Mesh mesh;
    private Material material;
    private Texture2D atlas;
    private Texture2D[] front;
    private int bearing,frame;
    private bool animate=true;
    private double previous;
    private string output;
    [MenuItem("Tools/People of the Iliac Bay/Keeper review scene")]
    public static void Open()
    {
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var window=GetWindow<KeeperReviewScene>("Keeper review");window.Setup();window.Show();
    }
    public static void Capture()
    {
        var window=GetWindow<KeeperReviewScene>("Keeper review");
        window.Setup();window.CaptureAll();
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_REVIEW_CAPTURED: "+window.output);
    }
    private void OnEnable() {EditorApplication.update+=Tick;}
    private void OnDisable() {EditorApplication.update-=Tick;}
    private void Tick()
    {
        if(!animate || !flat || EditorApplication.timeSinceStartup-previous<0.2) return;
        previous=EditorApplication.timeSinceStartup;frame=(frame+1)%12;Apply();SceneView.RepaintAll();Repaint();
    }
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Arrow = fixed keeper heading (+Z). No mirroring.");
        bearing=EditorGUILayout.IntSlider("Camera bearing",bearing,0,7);
        frame=EditorGUILayout.IntSlider("Native phase (5 fps)",frame,0,11);
        animate=EditorGUILayout.Toggle("Animate",animate);
        if(GUILayout.Button("Create / reset isolated scene")) Setup();
        if(flat) {Apply();if(GUILayout.Button("Capture all 8 bearings × 12 phases")) CaptureAll();}
        EditorGUILayout.HelpBox("0 front; 1 front-left; 2 left; 3 rear-left; 4 back; 5 rear-right; 6 right; 7 front-right. Compare face direction with the floor arrow and anatomical side labels.",MessageType.Info);
    }
    private void Setup()
    {
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        output=PeopleOfTheIliacBayPaths.Repository("work/keeper-review");Directory.CreateDirectory(output);Directory.CreateDirectory(Folder);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var art=new KeeperStandingArt(name=>File.ReadAllBytes(Path.Combine(PeopleOfTheIliacBayPaths.Repository("mod/Textures"),name)),true);
        atlas=art.Atlas;
        Store(atlas,Folder+"/shopkeeper-directions.asset");
        var reader=new TextureReader(Path.Combine(root,"GameFiles/arena2"));front=new Texture2D[12];
        for(int i=0;i<12;i++)
        {
            front[i]=reader.GetTexture2D(new GetTextureSettings {archive=182,record=0,frame=i,alphaIndex=0,stayReadable=true}).albedoMap;
            front[i].filterMode=FilterMode.Point;Store(front[i],Folder+"/native-front-"+i+".asset");
        }
        var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Neutral floor — fixed world axes";
        floor.transform.localScale=Vector3.one*0.5f;
        var floorMaterial=new Material(Shader.Find("Unlit/Color"));floorMaterial.color=new Color(0.13f,0.15f,0.17f);
        Store(floorMaterial,Folder+"/floor.asset");floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
        var arrow=new GameObject("FIXED FORWARD +Z");var line=arrow.AddComponent<LineRenderer>();
        line.positionCount=5;line.SetPositions(new[]{new Vector3(0,.02f,-.35f),new Vector3(0,.02f,1.65f),new Vector3(-.18f,.02f,1.35f),new Vector3(0,.02f,1.65f),new Vector3(.18f,.02f,1.35f)});
        line.startWidth=line.endWidth=.045f;
        var arrowMaterial=new Material(Shader.Find("Unlit/Color"));arrowMaterial.color=Color.yellow;
        Store(arrowMaterial,Folder+"/arrow.asset");line.sharedMaterial=arrowMaterial;
        Label("FORWARD +Z",new Vector3(0,.04f,1.9f),Color.yellow);
        Label("LEFT",new Vector3(-1.5f,.04f,0),Color.cyan);
        Label("RIGHT",new Vector3(1.5f,.04f,0),Color.magenta);
        Label("BACK",new Vector3(0,.04f,-1.5f),Color.white);
        var keeper=new GameObject("Shopkeeper — fixed world heading +Z");flat=keeper.transform;flat.position=Vector3.up*.95f;
        mesh=new Mesh {name="Keeper review native billboard winding"};
        mesh.vertices=new[]{new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,-.5f,0),new Vector3(-.5f,-.5f,0)};
        mesh.triangles=new[]{0,1,2,3,2,1};mesh.uv=new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)};mesh.RecalculateBounds();
        Store(mesh,Folder+"/billboard.asset");keeper.AddComponent<MeshFilter>().sharedMesh=mesh;
        material=new Material(Shader.Find("Unlit/Transparent Cutout"));Store(material,Folder+"/keeper.asset");
        keeper.AddComponent<MeshRenderer>().sharedMaterial=material;
        var cameraObject=new GameObject("Orbit camera — keeper heading stays fixed");camera=cameraObject.AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.085f,.10f);camera.orthographic=true;camera.orthographicSize=1.7f;
        Apply();EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),Folder+"/KeeperReview.unity");AssetDatabase.SaveAssets();
        WritePixelDifferences();
    }
    private static void Store(UnityEngine.Object value,string path)
    {
        if(AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(value,path);
    }
    private static void Label(string text,Vector3 position,Color color)
    {
        var label=new GameObject(text);label.transform.position=position;label.transform.rotation=Quaternion.Euler(90,0,0);
        var mesh=label.AddComponent<TextMesh>();mesh.text=text;mesh.characterSize=.08f;mesh.fontSize=48;mesh.anchor=TextAnchor.MiddleCenter;mesh.color=color;
    }
    private void Apply()
    {
        if(!flat || !camera || !material) return;
        var direction=Quaternion.AngleAxis(-45*bearing,Vector3.up)*Vector3.forward;
        camera.transform.position=direction*3+Vector3.up*2.3f;camera.transform.LookAt(Vector3.up*.75f);
        int actual=KeeperStandingArt.ViewIndex(direction,Vector3.forward);
        if(actual!=bearing) throw new InvalidOperationException("Review bearing disagrees with production selector.");
        flat.rotation=Quaternion.LookRotation(-new Vector3(camera.transform.forward.x,0,camera.transform.forward.z));
        if(actual==0)
        {
            material.mainTexture=front[frame];material.mainTextureScale=Vector2.one;material.mainTextureOffset=Vector2.zero;
            flat.localScale=new Vector3(1.9f*47/76,1.9f,1);
        }
        else
        {
            material.mainTexture=atlas;material.mainTextureScale=new Vector2(1/12f,1/7f);material.mainTextureOffset=new Vector2(frame/12f,(7-actual)/7f);
            float size=1.9f*96/76;flat.localScale=new Vector3(size,size,1);
        }
    }
    private void CaptureAll()
    {
        var target=new RenderTexture(512,512,24);camera.targetTexture=target;
        var readback=new Texture2D(512,512,TextureFormat.RGB24,false);var prior=RenderTexture.active;
        try
        {
            for(bearing=0;bearing<8;bearing++) for(frame=0;frame<12;frame++)
            {
                Apply();camera.Render();RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,512,512),0,0);readback.Apply();
                File.WriteAllBytes(Path.Combine(output,"angle-"+bearing+"-phase-"+frame.ToString("D2")+".png"),readback.EncodeToPNG());
            }
        }
        finally {camera.targetTexture=null;RenderTexture.active=prior;target.Release();DestroyImmediate(target);DestroyImmediate(readback);bearing=frame=0;Apply();}
    }
    private void WritePixelDifferences()
    {
        var pixels=atlas.GetPixels32();var rows=new List<string>{"view,phase,changed_pixels_from_phase0"};
        for(int view=1;view<=7;view++) for(int phase=0;phase<12;phase++)
        {
            int changed=0;
            for(int y=0;y<96;y++) for(int x=0;x<96;x++)
            {
                int row=((7-view)*96+y)*atlas.width;
                if(!pixels[row+x].Equals(pixels[row+phase*96+x])) changed++;
            }
            rows.Add(view+","+phase+","+changed);
        }
        File.WriteAllLines(Path.Combine(output,"pixel-differences.csv"),rows);
    }
}
