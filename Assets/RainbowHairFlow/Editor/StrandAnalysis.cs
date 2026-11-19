using System;
using System.Collections.Generic;
using UnityEngine;

namespace RainbowHairFlow.Editor
{
    public enum PathMapping { SurfaceDistance, UV_V, UV_U }
    [Serializable]
    public class Strand
    {
        public string name;
        public int[] triangles;
        public int[] vertices;
        public Vector3 center;
        public bool selected = true, reverse;
    }
    public static class StrandAnalysis
    {
        public static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer s) return s.sharedMesh;
            var f = r ? r.GetComponent<MeshFilter>() : null;
            return f ? f.sharedMesh : null;
        }
        public static void SetMesh(Renderer r, Mesh m)
        {
            if (r is SkinnedMeshRenderer s) s.sharedMesh = m;
            else r.GetComponent<MeshFilter>().sharedMesh = m;
        }
        public static List<Strand> Analyze(Mesh mesh, int slot)
        {
            if (!mesh || slot < 0 || slot >= mesh.subMeshCount) throw new ArgumentException("请选择有效的头发网格和材质槽。");
            var v = mesh.vertices; var tri = mesh.GetTriangles(slot);
            var parent = new int[v.Length]; for (int i=0;i<parent.Length;i++) parent[i]=i;
            Func<int,int> find = null; find = x => parent[x] == x ? x : (parent[x] = find(parent[x]));
            for (int i=0;i<tri.Length;i+=3) { parent[find(tri[i+1])] = find(tri[i]); parent[find(tri[i+2])] = find(tri[i]); }
            var groups = new Dictionary<int,List<int>>();
            foreach (int i in tri) { int k=find(i); if (!groups.ContainsKey(k)) groups[k]=new List<int>(); groups[k].Add(i); }
            var result = new List<Strand>();
            foreach (var g in groups.Values)
            {
                var ids = new HashSet<int>(g); var center=Vector3.zero; foreach(int i in ids) center+=v[i]; center/=ids.Count;
                result.Add(new Strand { triangles=g.ToArray(), vertices=new List<int>(ids).ToArray(),center=center,selected=g.Count>=9 });
            }
            result.Sort((a,b)=> { int x=a.center.x.CompareTo(b.center.x); return x!=0?x:a.center.y.CompareTo(b.center.y); });
            for(int i=0;i<result.Count;i++) result[i].name="发束 "+(i+1)+" · "+result[i].triangles.Length/3+" 面";
            return result;
        }
        // Deterministic: alternate left/right, then distribute picks over each side.
        public static void Balanced(List<Strand> strands, int count, int seed)
        {
            var left=new List<Strand>(); var right=new List<Strand>();
            float center=0; foreach(var s in strands) center+=s.center.x; center/=Mathf.Max(1,strands.Count);
            foreach(var s in strands) { s.selected=false; if(s.triangles.Length>=9) (s.center.x<center?left:right).Add(s); }
            var random=new System.Random(seed);
            Action<List<Strand>> shuffle = list => { for(int i=list.Count-1;i>0;i--) { int j=random.Next(i+1); var t=list[i];list[i]=list[j];list[j]=t; } };
            shuffle(left);shuffle(right); int l=0,r=0;
            for(int i=0;i<count;i++) { bool useLeft=(i%2==0&&l<left.Count)||r>=right.Count; if(useLeft&&l<left.Count) left[l++].selected=true; else if(r<right.Count) right[r++].selected=true; }
        }
        public static Mesh Build(Mesh source, List<Strand> strands, PathMapping mapping, Vector3 rootDirection)
        {
            var occupied=new List<Vector4>(); source.GetUVs(7,occupied);
            if(occupied.Count>0) throw new InvalidOperationException("源网格 UV7 已占用。为保护原效果，停止生成；请先使用单独的头发网格。");
            var mesh=UnityEngine.Object.Instantiate(source); mesh.name=source.name+" Rainbow Flow";
            var paths=new List<Vector4>(new Vector4[source.vertexCount]); var triangles=new List<int>();
            var chosen=strands.FindAll(s=>s.selected);
            if(chosen.Count==0) { UnityEngine.Object.DestroyImmediate(mesh); throw new InvalidOperationException("至少选择一束头发。"); }
            // Pair nearest mirrored strands instead of assigning all left IDs before all right IDs.
            var ordered=new List<Strand>(); var pool=new List<Strand>(chosen);
            float center=source.bounds.center.x;
            while(pool.Count>0)
            {
                var a=pool[0];pool.RemoveAt(0);ordered.Add(a);
                if(pool.Count==0) break;
                Vector3 mirror=a.center;mirror.x=2*center-mirror.x;
                int best=0;for(int i=1;i<pool.Count;i++) if((pool[i].center-mirror).sqrMagnitude<(pool[best].center-mirror).sqrMagnitude) best=i;
                ordered.Add(pool[best]);pool.RemoveAt(best);
            }
            for(int i=0;i<ordered.Count;i++)
            {
                var s=ordered[i]; Encode(source,s,mapping,rootDirection,paths,i);triangles.AddRange(s.triangles);
            }
            mesh.SetUVs(7,paths);mesh.subMeshCount=source.subMeshCount+1;mesh.SetTriangles(triangles,source.subMeshCount,false);
            return mesh;
        }
        public static Mesh FromApproved(Mesh source, int flowSlot)
        {
            if(flowSlot<0||flowSlot>=source.subMeshCount) throw new ArgumentException("认可版效果槽无效。");
            var uv=new List<Vector4>(); source.GetUVs(3,uv);
            if(uv.Count!=source.vertexCount) throw new InvalidOperationException("该网格没有认可版流光标定。");
            var mesh=UnityEngine.Object.Instantiate(source);mesh.name="Approved Rainbow Flow";mesh.SetUVs(7,uv);return mesh;
        }
        static void Encode(Mesh mesh,Strand strand,PathMapping mapping,Vector3 direction,List<Vector4> output,int id)
        {
            var v=mesh.vertices;var uv=mesh.uv;var values=new Dictionary<int,float>();
            float min=float.PositiveInfinity,max=float.NegativeInfinity;
            if(mapping==PathMapping.SurfaceDistance)
            {
                direction=direction.sqrMagnitude<1e-8f?Vector3.up:direction.normalized;
                int root=strand.vertices[0];foreach(int i in strand.vertices) if(Vector3.Dot(v[i],direction)>Vector3.Dot(v[root],direction)) root=i;
                var edges=new Dictionary<int,Dictionary<int,float>>();foreach(int i in strand.vertices) edges[i]=new Dictionary<int,float>();
                for(int t=0;t<strand.triangles.Length;t+=3) for(int e=0;e<3;e++)
                {
                    int a=strand.triangles[t+e],b=strand.triangles[t+(e+1)%3];float w=Vector3.Distance(v[a],v[b]);edges[a][b]=w;edges[b][a]=w;
                }
                foreach(int i in strand.vertices) values[i]=float.PositiveInfinity;
                values[root]=0;var heap=new MinHeap();heap.Push(root,0);
                while(heap.Count>0) { var n=heap.Pop();if(n.Item2>values[n.Item1])continue;foreach(var e in edges[n.Item1]) { float d=n.Item2+e.Value;if(d<values[e.Key]){values[e.Key]=d;heap.Push(e.Key,d);} } }
            }
            else
            {
                if(uv.Length!=v.Length) throw new InvalidOperationException("该网格没有 UV0，请使用表面路径模式。");
                foreach(int i in strand.vertices) values[i]=mapping==PathMapping.UV_V?uv[i].y:uv[i].x;
            }
            foreach(var x in values.Values) { min=Mathf.Min(min,x);max=Mathf.Max(max,x); }
            float widthMin=float.PositiveInfinity,widthMax=float.NegativeInfinity;
            foreach(int i in strand.vertices) { float w=uv.Length==v.Length?(mapping==PathMapping.UV_U?uv[i].y:uv[i].x):v[i].x;widthMin=Mathf.Min(widthMin,w);widthMax=Mathf.Max(widthMax,w); }
            foreach(int i in strand.vertices)
            {
                float along=(values[i]-min)/Mathf.Max(1e-6f,max-min);
                // UV maps conventionally put roots at V=1. Surface distance starts at the root.
                if(mapping!=PathMapping.SurfaceDistance) along=1-along;
                if(strand.reverse)along=1-along;
                float w=uv.Length==v.Length?(mapping==PathMapping.UV_U?uv[i].y:uv[i].x):v[i].x;
                output[i]=new Vector4(along,(w-widthMin)/Mathf.Max(1e-6f,widthMax-widthMin),id,0);
            }
        }
        sealed class MinHeap
        {
            readonly List<Tuple<int,float>> a=new List<Tuple<int,float>>(); public int Count=>a.Count;
            public void Push(int id,float d) { var n=Tuple.Create(id,d);a.Add(n);int i=a.Count-1;while(i>0){int p=(i-1)/2;if(a[p].Item2<=d)break;a[i]=a[p];i=p;}a[i]=n; }
            public Tuple<int,float> Pop(){var n=a[0];var last=a[a.Count-1];a.RemoveAt(a.Count-1);if(a.Count==0)return n;int i=0;while(i*2+1<a.Count){int c=i*2+1;if(c+1<a.Count&&a[c+1].Item2<a[c].Item2)c++;if(a[c].Item2>=last.Item2)break;a[i]=a[c];i=c;}a[i]=last;return n;}
        }
    }
}
