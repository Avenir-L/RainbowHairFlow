using UnityEngine;
namespace RainbowHairFlow.Editor {
    public sealed class FlowInstallRecord : ScriptableObject
    {
        public Mesh originalMesh, generatedMesh;
        public Material[] originalMaterials;
        public string rendererPath, installerName, parameter;
    }
}
