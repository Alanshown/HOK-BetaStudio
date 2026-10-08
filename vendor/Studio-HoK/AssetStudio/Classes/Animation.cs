using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AssetStudio
{
    public sealed class Animation : Behaviour
    {
        public PPtr<AnimationClip> m_Animation;
        public List<PPtr<AnimationClip>> m_Animations;
        public int m_WrapMode;
        public bool m_PlayAutomatically;
        public bool m_AnimatePhysics;
        public int m_CullingType;

        public Animation(ObjectReader reader) : base(reader)
        {
            m_Animation = new PPtr<AnimationClip>(reader);
            int numAnimations = reader.ReadInt32();
            m_Animations = new List<PPtr<AnimationClip>>();
            for (int i = 0; i < numAnimations; i++)
            {
                m_Animations.Add(new PPtr<AnimationClip>(reader));
            }
            if (version[0] >= 5)
            {
                m_WrapMode = reader.ReadInt32();
                m_PlayAutomatically = reader.ReadBoolean();
                m_AnimatePhysics = reader.ReadBoolean();
                reader.AlignStream();
                m_CullingType = reader.ReadInt32();
            }
        }
    }
}
