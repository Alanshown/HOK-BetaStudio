using AssetStudio;
using Hok.Worker;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Obj = AssetStudio.Object;

// Synthetic source graph with exact expected geometry, joints and keyframes.
// The native FBX outputs are then re-imported by test-model-fixtures.cjs.
var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var checks=new List<string>();
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);Console.WriteLine("PASS "+message);}
T Empty<T>() where T:class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
var manager=new AssetsManager{Game=GameManager.GetGame(GameType.HonorOfKings)};
var file=Empty<SerializedFile>();file.assetsManager=manager;file.game=manager.Game;file.fullName="synthetic-model";file.fileName="synthetic-model";file.Objects=[];file.ObjectsDic=[];file.m_Externals=[];manager.assetsFileList.Add(file);
long next=1;
T Add<T>(ClassIDType type) where T:Obj {var obj=Empty<T>();obj.assetsFile=file;obj.type=type;obj.m_PathID=next++;file.Objects.Add(obj);file.ObjectsDic.Add(obj.m_PathID,obj);return obj;}
PPtr<T> Ptr<T>(T? obj) where T:Obj=>new(0,obj?.m_PathID??0,file);
var mesh=Add<Mesh>(ClassIDType.Mesh);mesh.m_Name="Triangle";mesh.m_VertexCount=3;mesh.m_Vertices=[0,0,0,1,0,0,0,1,0];mesh.m_UV0=[0,0,1,0,0,1];mesh.m_Normals=[0,0,1,0,0,1,0,0,1];mesh.m_Indices=[0,1,2];mesh.m_BindPose=[Matrix4x4.Scale(Vector3.One)];mesh.m_BoneNameHashes=[];
var sub=Empty<SubMesh>();sub.indexCount=3;sub.vertexCount=3;mesh.m_SubMeshes=[sub];
mesh.m_Skin=Enumerable.Range(0,3).Select(_=>new BoneWeights4{boneIndex=[0,0,0,0],weight=[1,0,0,0]}).ToList();
Check(Exporters.Formats(mesh).Contains("fbx"),"Mesh explicitly offers FBX");
var standalone=Path.Combine(output,"standalone.fbx");var warnings=Exporters.Write(mesh,"fbx",standalone,new());Check(warnings.Any(w=>w.Contains("No related renderer")),"Standalone geometry does not invent a skeleton");
var go=Add<GameObject>(ClassIDType.GameObject);go.m_Name="Rig";go.m_Components=[];
var transform=Add<Transform>(ClassIDType.Transform);go.m_Transform=transform;transform.m_GameObject=Ptr(go);transform.m_Father=Ptr<Transform>(null);transform.m_Children=[];transform.m_LocalRotation=new(0,0,0,1);transform.m_LocalScale=Vector3.One;
var boneGo=Add<GameObject>(ClassIDType.GameObject);boneGo.m_Name="Joint";boneGo.m_Components=[];
var bone=Add<Transform>(ClassIDType.Transform);boneGo.m_Transform=bone;bone.m_GameObject=Ptr(boneGo);bone.m_Father=Ptr(transform);bone.m_Children=[];bone.m_LocalRotation=new(0,0,0,1);bone.m_LocalScale=Vector3.One;transform.m_Children=[Ptr(bone)];
var skin=Add<SkinnedMeshRenderer>(ClassIDType.SkinnedMeshRenderer);skin.m_GameObject=Ptr(go);skin.m_Mesh=Ptr(mesh);skin.m_Bones=[Ptr(bone)];skin.m_Materials=[];go.m_SkinnedMeshRenderer=skin;
AnimationClip Clip(string name,float distance){var c=Add<AnimationClip>(ClassIDType.AnimationClip);c.m_Name=name;c.m_Legacy=true;c.m_SampleRate=30;c.m_CompressedRotationCurves=[];c.m_RotationCurves=[];c.m_PositionCurves=[];c.m_ScaleCurves=[];c.m_EulerCurves=[];c.m_FloatCurves=[];c.m_PPtrCurves=[];
var curve=new Vector3Curve("");curve.curve.m_Curve=[new(0,Vector3.Zero,Vector3.Zero,Vector3.Zero,Vector3.Zero),new(1,new Vector3(distance,0,0),Vector3.Zero,Vector3.Zero,Vector3.Zero)];c.m_PositionCurves.Add(curve);return c;}
var clip=Clip("Move",2);var alternate=Clip("Other",5);
var animation=Add<Animation>(ClassIDType.Animation);animation.m_GameObject=Ptr(go);animation.m_Animation=Ptr(clip);animation.m_Animations=[Ptr(alternate)];go.m_Animation=animation;
ModelConverter.Options Options(bool collect)=>new(){game=manager.Game,imageFormat=ImageFormat.Png,collectAnimations=collect,exportMaterials=false,materials=[],uvs=Enumerable.Range(0,8).ToDictionary(i=>"UV"+i,i=>(true,i)),texs=[]};
var model=new ModelConverter(go,Options(true));Check(model.AnimationList.Count==2,"Default Animation pointer is included even when absent from m_Animations");Check(model.AnimationList.All(a=>a.TrackList.All(t=>t.Path=="Rig")),"Root transform curves bind to their actual hierarchy node");
Check(new ModelConverter(go,Options(false)).AnimationList.Count==0,"Disabling animations skips legacy clip collection");
Check(new ModelConverter(go,Options(false),[clip]).AnimationList.Single().Name=="Move","Explicit clip selection excludes unrelated legacy animations");
Exporters.Write(mesh,"fbx",Path.Combine(output,"rigged.fbx"),new());
Exporters.Write(mesh,"fbx",Path.Combine(output,"static.fbx"),new(){Animations=false});
Exporters.Write(clip,"fbx",Path.Combine(output,"clip.fbx"),new());
Exporters.Write(mesh,"obj",Path.Combine(output,"triangle.obj"),new());
skin.m_Bones=[Ptr(transform)];
Exporters.Write(mesh,"fbx",Path.Combine(output,"joint-mesh.fbx"),new());
var combined=new ModelConverter(go,Options(true));
var combinedTrack=combined.AnimationList.First(a=>a.Name=="Move").TrackList.Single();
combinedTrack.BlendShape=new ImportedBlendShape{ChannelName="Smile",Keyframes=[new(0,0),new(1,100)]};
typeof(Fbx.Exporter).GetMethod("SeparateJointGeometry",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,[combined]);
Check(combinedTrack.Path=="Rig"&&combinedTrack.Translations.Count>0&&combinedTrack.BlendShape==null&&
    combined.AnimationList.First(a=>a.Name=="Move").TrackList.Any(t=>t.Path=="Rig/Geometry"&&t.BlendShape!=null&&t.Translations.Count==0),
    "Joint motion stays on the joint when a combined morph track moves to a geometry child");
skin.m_Bones=[Ptr(bone)];
var controller=Add<AnimatorController>(ClassIDType.AnimatorController);controller.m_AnimationClips=[Ptr(clip)];
var replacement=Add<AnimatorOverrideController>(ClassIDType.AnimatorOverrideController);replacement.m_Controller=new PPtr<RuntimeAnimatorController>(0,controller.m_PathID,file);
var clipPair=Empty<AnimationClipOverride>();clipPair.m_OriginalClip=Ptr(clip);clipPair.m_OverrideClip=Ptr(alternate);replacement.m_Clips=[clipPair];
var animator=Add<Animator>(ClassIDType.Animator);animator.m_GameObject=Ptr(go);animator.m_Avatar=Ptr<Avatar>(null);animator.m_HasTransformHierarchy=true;animator.m_Controller=new PPtr<RuntimeAnimatorController>(0,replacement.m_PathID,file);go.m_Animator=animator;go.m_Animation=null;
Check(new ModelConverter(go,Options(true)).AnimationList.Single().Name=="Other","AnimatorOverrideController exports the replacement clip, not its original");
Exporters.Write(mesh,"fbx",Path.Combine(output,"override.fbx"),new());
go.m_Animator=null;go.m_Animation=animation;
var absent=Clip("Unreferenced",9);try{Exporters.Write(absent,"fbx",Path.Combine(output,"unrelated.fbx"),new());throw new Exception("Unlinked animation accepted");}catch(InvalidDataException){Check(true,"Unreferenced clip is not paired with an unrelated model");}
mesh.m_Vertices[0]=float.NaN;try{Exporters.Write(mesh,"obj",Path.Combine(output,"invalid.obj"),new());throw new Exception("NaN accepted");}catch(InvalidDataException){Check(true,"Invalid geometry cannot be reported as a successful OBJ");}mesh.m_Vertices[0]=0;
File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks}));
