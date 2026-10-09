using System;

namespace AssetStudio
{
    // Object-table existence is independent of typed parser success. Never use
    // the instantiated Object list as the discovery authority.
    public sealed class ObjectParseStatus
    {
        public string Status { get; set; }
        public string Parser { get; set; }
        public long ConsumedBytes { get; set; }
        public long RemainingBytes { get; set; }
        public string Error { get; set; }
        public bool Typed { get; set; }
        public static string ClassName(int classId,SerializedType serializedType=null) => Enum.IsDefined(typeof(ClassIDType), classId)
            ? ((ClassIDType)classId).ToString() : serializedType?.m_Type?.m_Nodes?.Count>0
            ? serializedType.m_Type.m_Nodes[0].m_Type : "UnknownClass_" + classId;
    }
}
