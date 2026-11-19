using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RainbowHairFlow.Editor
{
    public sealed class FlowWindow : EditorWindow
    {
        Renderer hair;Transform avatar;UnityEngine.Object menu;
        List<Strand> strands=new List<Strand>();int slot,seed=7,count=24;PathMapping mapping;Vector3 rootDirection=Vector3.up;
        float speed=1,width=1,brightness=1;Vector2 scroll;string folder="Assets/RainbowHairFlowGenerated/MyHair";
        GameObject preview;Material previewMaterial;Mesh previewMesh;double previewStart;FlowInstallRecord record;string error;bool approved;
        [MenuItem("Tools/彩虹发丝流光/打开工具")]
        public static void Open()=>GetWindow<FlowWindow>("彩虹发丝流光");
        [MenuItem("Tools/彩虹发丝流光/反馈 Bug")]
        static void ReportBug()
        {
            const string repository = "Avenir-L/RainbowHairFlow";
            string environment = "Rainbow Hair Flow 0.3.0 / Unity " + Application.unityVersion
                + " / " + EditorUserBuildSettings.activeBuildTarget;
            Application.OpenURL("https://github.com/" + repository
                + "/issues/new?template=bug.yml&environment=" + Uri.EscapeDataString(environment));
        }
        void OnEnable(){SceneView.duringSceneGui+=SceneGUI;EditorApplication.update+=Tick;}
        void OnDisable(){SceneView.duringSceneGui-=SceneGUI;EditorApplication.update-=Tick;ClearPreview();}
        void Tick()
        {
            if(!preview)return;
            if(previewMaterial)previewMaterial.SetFloat("_PreviewTime",(float)(EditorApplication.timeSinceStartup-previewStart));
            if(hair is SkinnedMeshRenderer src&&preview.TryGetComponent<SkinnedMeshRenderer>(out var dst)) for(int i=0;i<src.sharedMesh.blendShapeCount;i++)dst.SetBlendShapeWeight(i,src.GetBlendShapeWeight(i));
            SceneView.RepaintAll();
        }
        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Rainbow Hair Flow   0.3.0", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("反馈 Bug ↗", EditorStyles.toolbarButton))
                    ReportBug();
            }
            EditorGUILayout.HelpBox("仅支持 Unity Built-in 渲染管线。使用克隆网格附加效果，不修改原网格、原材质或骨骼。菜单安装依赖 SDK + Modular Avatar。",MessageType.Info);
            EditorGUI.BeginChangeCheck();var next=(Renderer)EditorGUILayout.ObjectField("头发 Renderer",hair,typeof(Renderer),true);
            if(EditorGUI.EndChangeCheck()){ClearPreview();hair=next;strands.Clear();slot=0;approved=false;if(hair){avatar=hair.transform;while(avatar.parent&&!avatar.GetComponent<Animator>())avatar=avatar.parent;rootDirection=hair.transform.InverseTransformDirection(avatar.up).normalized;}}
            avatar=(Transform)EditorGUILayout.ObjectField("模型根节点",avatar,typeof(Transform),true);
            menu=EditorGUILayout.ObjectField("安装到菜单（可选）",menu,typeof(ScriptableObject),false);
            EditorGUI.BeginChangeCheck();slot=EditorGUILayout.IntField("源头发材质槽（从0起）",slot);if(EditorGUI.EndChangeCheck()){ClearPreview();strands.Clear();}
            if(GUILayout.Button("分析发束"))Run(()=>{ClearPreview();strands=StrandAnalysis.Analyze(StrandAnalysis.MeshOf(hair),slot);});
            EditorGUI.BeginChangeCheck();mapping=(PathMapping)EditorGUILayout.EnumPopup("根梢标定",mapping);rootDirection=EditorGUILayout.Vector3Field("发根朝向（头发局部空间）",rootDirection);if(EditorGUI.EndChangeCheck())ClearPreview();
            EditorGUILayout.HelpBox("SurfaceDistance 沿连通三角形从最高点向梢端计算；UV_V / UV_U 按 UV 轴。模型整体连成一片时建议先拆分发束。场景中点击编号选择，列表可反转方向。",MessageType.None);
            count=EditorGUILayout.IntSlider("随机选择数量",count,1,Mathf.Max(1,strands.Count));seed=EditorGUILayout.IntField("随机种子",seed);
            if(GUILayout.Button("左右均衡随机选择")){ClearPreview();StrandAnalysis.Balanced(strands,count,seed);}
            EditorGUILayout.BeginHorizontal();if(GUILayout.Button("全选")){ClearPreview();strands.ForEach(s=>s.selected=true);}if(GUILayout.Button("清空")){ClearPreview();strands.ForEach(s=>s.selected=false);}EditorGUILayout.EndHorizontal();
            scroll=EditorGUILayout.BeginScrollView(scroll,GUILayout.MinHeight(100),GUILayout.MaxHeight(230));
            foreach(var s in strands){EditorGUILayout.BeginHorizontal();EditorGUI.BeginChangeCheck();s.selected=EditorGUILayout.ToggleLeft(s.name,s.selected);s.reverse=GUILayout.Toggle(s.reverse,"反向",GUILayout.Width(60));if(EditorGUI.EndChangeCheck())ClearPreview();EditorGUILayout.EndHorizontal();}EditorGUILayout.EndScrollView();
            EditorGUI.BeginChangeCheck();speed=EditorGUILayout.Slider("流速",speed,.1f,3);width=EditorGUILayout.Slider("流光宽度",width,.2f,2);brightness=EditorGUILayout.Slider("亮度",brightness,.1f,2);if(EditorGUI.EndChangeCheck()&&previewMaterial){previewMaterial.SetFloat("_Speed",speed);previewMaterial.SetFloat("_Width",width);previewMaterial.SetFloat("_Brightness",brightness);}
            approved=EditorGUILayout.Toggle(new GUIContent("导入已认可标定（高级）","仅用于已带 UV3 流光标定的旧网格，效果槽固定为最后一槽；不重新分析或随机选择。"),approved);
            EditorGUILayout.BeginHorizontal();if(GUILayout.Button("动态预览"))Run(Preview);if(GUILayout.Button("停止预览"))ClearPreview();EditorGUILayout.EndHorizontal();
            folder=EditorGUILayout.TextField("独立输出目录",folder);
            if(GUILayout.Button("生成并安装（默认关闭）"))Run(()=>{ClearPreview();var mesh=Generate();record=FlowInstaller.Install(avatar,hair,mesh,folder,mesh.subMeshCount-1,speed,width,brightness,menu,slot);Selection.activeObject=record;});
            record=(FlowInstallRecord)EditorGUILayout.ObjectField("撤销记录",record,typeof(FlowInstallRecord),false);
            using(new EditorGUI.DisabledScope(!record||!avatar))if(GUILayout.Button("按记录还原原头发并移除开关"))Run(()=>{ClearPreview();FlowInstaller.Uninstall(avatar,record);});
            if(!string.IsNullOrEmpty(error))EditorGUILayout.HelpBox(error,MessageType.Error);
        }
        Mesh Generate(){if(!hair)throw new InvalidOperationException("请先指定头发。");var m=StrandAnalysis.MeshOf(hair);return approved?StrandAnalysis.FromApproved(m,m.subMeshCount-1):StrandAnalysis.Build(m,strands,mapping,rootDirection);}
        void Run(Action action){try{error=null;action();}catch(Exception e){error=e.Message;Debug.LogException(e);}}
        void Preview()
        {
            ClearPreview();previewMesh=Generate();int[] triangles=previewMesh.GetTriangles(previewMesh.subMeshCount-1);previewMesh.subMeshCount=1;previewMesh.SetTriangles(triangles,0,false);
            previewMaterial=new Material(Shader.Find("RainbowHairFlow/Strand Rainbow")){hideFlags=HideFlags.HideAndDontSave};previewMaterial.SetFloat("_FlowEnabled",1);previewMaterial.SetFloat("_Speed",speed);previewMaterial.SetFloat("_Width",width);previewMaterial.SetFloat("_Brightness",brightness);
            preview=new GameObject("RainbowFlow Preview"){hideFlags=HideFlags.HideAndDontSave};preview.transform.SetParent(hair.transform,false);
            var baseMats=hair.sharedMaterials;if(slot>=0&&slot<baseMats.Length)FlowInstaller.ConfigureSurface(baseMats[slot],previewMaterial);
            Renderer target;if(hair is SkinnedMeshRenderer skin){var s=preview.AddComponent<SkinnedMeshRenderer>();s.sharedMesh=previewMesh;s.bones=skin.bones;s.rootBone=skin.rootBone;s.localBounds=skin.localBounds;s.updateWhenOffscreen=true;target=s;}else{preview.AddComponent<MeshFilter>().sharedMesh=previewMesh;target=preview.AddComponent<MeshRenderer>();}target.sharedMaterial=previewMaterial;previewStart=EditorApplication.timeSinceStartup;
        }
        void ClearPreview(){if(preview)DestroyImmediate(preview);if(previewMesh)DestroyImmediate(previewMesh);if(previewMaterial)DestroyImmediate(previewMaterial);preview=null;previewMesh=null;previewMaterial=null;SceneView.RepaintAll();}
        void SceneGUI(SceneView view)
        {
            if(!hair||strands.Count==0)return;
            for(int i=0;i<strands.Count;i++)
            {
                var s=strands[i];var p=hair.transform.TransformPoint(s.center);Handles.color=s.selected?Color.cyan:Color.gray;
                if(Handles.Button(p,Quaternion.identity,HandleUtility.GetHandleSize(p)*.025f,HandleUtility.GetHandleSize(p)*.035f,Handles.DotHandleCap)){s.selected=!s.selected;ClearPreview();Repaint();}
                Handles.Label(p,(i+1)+(s.reverse?" ↥":" ↧"));
            }
        }
    }
}
