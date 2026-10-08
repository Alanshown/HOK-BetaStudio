using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AssetStudio
{
    public sealed class RectTransform : Transform
    {
        public Vector2 m_AnchorMin, m_AnchorMax, m_AnchoredPosition, m_SizeDelta, m_Pivot;
        public RectTransform(ObjectReader reader) : base(reader)
        {
            m_AnchorMin = reader.ReadVector2();
            m_AnchorMax = reader.ReadVector2();
            m_AnchoredPosition = reader.ReadVector2();
            m_SizeDelta = reader.ReadVector2();
            m_Pivot = reader.ReadVector2();
        }
    }
}
