using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RainbowHairFlow.Editor
{
    public static class FlowInstaller
    {
        public static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name)).FirstOrDefault(t=>t!=null);
        static void Field(object o,string n,object v) { var f=o.GetType().GetField(n);if(f==null)throw new InvalidOperationException("依赖版本不兼容，缺少字段："+n); f.SetValue(o,v); }
        static void EnumField(object o,string n,string value) { var f=o.GetType().GetField(n);f.SetValue(o,Enum.Parse(f.FieldType,value)); }
        public static void ConfigureSurface(Material source,Material target)
        {
            if(!source)return;
            target.SetFloat("_UseAlpha",source.renderQueue>=2450?1:0);
            target.SetFloat("_BaseAlpha",source.HasProperty("_Color")?source.GetColor("_Color").a:1);
            if(source.HasProperty("_MainTex")){target.SetTexture("_MaskTex",source.GetTexture("_MainTex"));target.SetTextureScale("_MaskTex",source.GetTextureScale("_MainTex"));target.SetTextureOffset("_MaskTex",source.GetTextureOffset("_MainTex"));}
            if(source.shader.name.Contains("Cutout")&&source.HasProperty("_Cutoff"))target.SetFloat("_AlphaCutoff",source.GetFloat("_Cutoff"));
            if(source.HasProperty("_AlphaMaskMode"))
            {
                target.SetFloat("_MaskMode",source.GetFloat("_AlphaMaskMode"));
                target.SetFloat("_MaskScale",source.GetFloat("_AlphaMaskScale"));target.SetFloat("_MaskValue",source.GetFloat("_AlphaMaskValue"));
                target.SetTexture("_SeparateMask",source.GetTexture("_AlphaMask"));target.SetTextureScale("_SeparateMask",source.GetTextureScale("_AlphaMask"));target.SetTextureOffset("_SeparateMask",source.GetTextureOffset("_AlphaMask"));
            }
        }
        public static FlowInstallRecord Install(Transform avatar,Renderer hair,Mesh generated,string folder,int slot,float speed,float width,float brightness, UnityEngine.Object menuTarget=null, int sourceSlot=0)
        {
            if(!avatar||!hair||!hair.transform.IsChildOf(avatar)) throw new InvalidOperationException("头发必须位于所选模型下。");
            var merge=Find("nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator");
            var parameters=Find("nadena.dev.modular_avatar.core.ModularAvatarParameters");
            var installer=Find("nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller");
            var menuType=Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu");
            if(merge==null||parameters==null||installer==null||menuType==null) throw new InvalidOperationException("自动接入需要 VRChat Avatars SDK 和 Modular Avatar。请先安装依赖。");
            if(menuTarget&& !menuType.IsInstanceOfType(menuTarget))throw new InvalidOperationException("目标菜单必须是 VRCExpressionsMenu。");
            if(!folder.StartsWith("Assets/")||AssetDatabase.IsValidFolder(folder)) throw new InvalidOperationException("请使用 Assets 下尚不存在的独立输出目录。");
            if(hair.sharedMaterials.Any(m=>m&&m.shader&&m.shader.name=="RainbowHairFlow/Strand Rainbow"))throw new InvalidOperationException("该头发已安装通用流光，请先撤销上一次安装。");
            var shader=Shader.Find("RainbowHairFlow/Strand Rainbow");if(!shader)throw new InvalidOperationException("未找到流光 shader。");
            int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("安装彩虹流光");
            var oldMesh=StrandAnalysis.MeshOf(hair);var oldMats=hair.sharedMaterials;GameObject go=null;
            try
            {
                EnsureFolder(folder); string parameter="RainbowHairFlow/"+Guid.NewGuid().ToString("N").Substring(0,8);
                AssetDatabase.CreateAsset(generated,folder+"/Hair.asset");
                var on=new Material(shader){name="Rainbow Flow On",enableInstancing=true};on.SetFloat("_FlowEnabled",1);on.SetFloat("_PreviewTime",-1);on.SetFloat("_Speed",speed);on.SetFloat("_Width",width);on.SetFloat("_Brightness",brightness);
                if(sourceSlot>=0&&sourceSlot<oldMats.Length)ConfigureSurface(oldMats[sourceSlot],on);
                var off=new Material(on){name="Rainbow Flow Off"};off.SetFloat("_FlowEnabled",0);
                AssetDatabase.CreateAsset(on,folder+"/On.mat");AssetDatabase.CreateAsset(off,folder+"/Off.mat");
                string path=AnimationUtility.CalculateTransformPath(hair.transform,avatar);
                var offClip=Clip(path,hair.GetType(),slot,off);var onClip=Clip(path,hair.GetType(),slot,on);
                AssetDatabase.CreateAsset(offClip,folder+"/Off.anim");AssetDatabase.CreateAsset(onClip,folder+"/On.anim");
                var controller=AnimatorController.CreateAnimatorControllerAtPath(folder+"/Flow.controller");controller.AddParameter(parameter,AnimatorControllerParameterType.Bool);
                var sm=controller.layers[0].stateMachine;var a=sm.AddState("Off");var b=sm.AddState("On");a.motion=offClip;b.motion=onClip;a.writeDefaultValues=b.writeDefaultValues=false;sm.defaultState=a;
                Transition(a,b,parameter,true);Transition(b,a,parameter,false);
                go=new GameObject("Rainbow Hair Flow "+parameter.Substring(parameter.Length-8));go.transform.SetParent(avatar,false);Undo.RegisterCreatedObjectUndo(go,"安装彩虹流光");
                var ma=go.AddComponent(merge);Field(ma,"animator",controller);EnumField(ma,"layerType","FX");EnumField(ma,"pathMode","Absolute");Field(ma,"matchAvatarWriteDefaults",true);
                var pa=go.AddComponent(parameters);var list=(IList)parameters.GetField("parameters").GetValue(pa);var config=Activator.CreateInstance(list.GetType().GetGenericArguments()[0]);Field(config,"nameOrPrefix",parameter);EnumField(config,"syncType","Bool");Field(config,"saved",true);Field(config,"hasExplicitDefaultValue",true);list.Add(config);EditorUtility.SetDirty(pa);
                var menu=ScriptableObject.CreateInstance(menuType);menu.name="彩虹流光";
                var controls=(IList)menuType.GetField("controls").GetValue(menu);var control=Activator.CreateInstance(controls.GetType().GetGenericArguments()[0]);Field(control,"name","彩虹流光");EnumField(control,"type","Toggle");Field(control,"value",1f);var pf=control.GetType().GetField("parameter");var param=Activator.CreateInstance(pf.FieldType);Field(param,"name",parameter);pf.SetValue(control,param);controls.Add(control);AssetDatabase.CreateAsset(menu,folder+"/Menu.asset");
                var mi=go.AddComponent(installer);Field(mi,"menuToAppend",menu);Field(mi,"installTargetMenu",menuTarget);
                var record=ScriptableObject.CreateInstance<FlowInstallRecord>();record.originalMesh=oldMesh;record.generatedMesh=generated;record.originalMaterials=oldMats;record.rendererPath=path;record.installerName=go.name;record.parameter=parameter;AssetDatabase.CreateAsset(record,folder+"/InstallRecord.asset");
                Undo.RecordObject(hair,"安装彩虹流光");if(!(hair is SkinnedMeshRenderer))Undo.RecordObject(hair.GetComponent<MeshFilter>(),"安装彩虹流光");
                StrandAnalysis.SetMesh(hair,generated);var mats=new Material[generated.subMeshCount];Array.Copy(oldMats,mats,Math.Min(oldMats.Length,mats.Length));mats[slot]=off;hair.sharedMaterials=mats;
                EditorUtility.SetDirty(hair);PrefabUtility.RecordPrefabInstancePropertyModifications(hair);
                PrefabUtility.SaveAsPrefabAsset(go,folder+"/MenuInstaller.prefab");
                AssetDatabase.SaveAssets();Undo.CollapseUndoOperations(group);return record;
            }
            catch
            {
                StrandAnalysis.SetMesh(hair,oldMesh);hair.sharedMaterials=oldMats;if(go)UnityEngine.Object.DestroyImmediate(go);
                if(AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);throw;
            }
        }
        public static void Uninstall(Transform avatar,FlowInstallRecord record)
        {
            var target=avatar.Find(record.rendererPath);if(!target)throw new InvalidOperationException("找不到原头发路径，未修改任何内容。");
            var hair=target.GetComponent<Renderer>();if(StrandAnalysis.MeshOf(hair)!=record.generatedMesh)throw new InvalidOperationException("当前网格与记录不匹配，拒绝覆盖。");
            Undo.RecordObject(hair,"撤销流光");if(!(hair is SkinnedMeshRenderer))Undo.RecordObject(hair.GetComponent<MeshFilter>(),"撤销流光");StrandAnalysis.SetMesh(hair,record.originalMesh);hair.sharedMaterials=record.originalMaterials;
            var obj=avatar.Find(record.installerName);if(obj)Undo.DestroyObjectImmediate(obj.gameObject);EditorUtility.SetDirty(hair);
        }
        static AnimationClip Clip(string path,Type type,int slot,Material material)
        {
            var clip=new AnimationClip();AnimationUtility.SetObjectReferenceCurve(clip,new EditorCurveBinding{path=path,type=type,propertyName="m_Materials.Array.data["+slot+"]"},new[]{new ObjectReferenceKeyframe{time=0,value=material}});return clip;
        }
        static void Transition(AnimatorState a,AnimatorState b,string p,bool on){var t=a.AddTransition(b);t.hasExitTime=false;t.duration=0;t.AddCondition(on?AnimatorConditionMode.If:AnimatorConditionMode.IfNot,0,p);}
        public static void EnsureFolder(string path){var parts=path.Split('/');string p=parts[0];for(int i=1;i<parts.Length;i++){if(!AssetDatabase.IsValidFolder(p+"/"+parts[i]))AssetDatabase.CreateFolder(p,parts[i]);p+="/"+parts[i];}}
    }
}
