# Validated HOK structure profiles

The compressed JSON contains factual field layouts derived from the [AssetRipper TypeTreeDumps 2022.3.5f1 release-layout data](https://github.com/AssetRipper/TypeTreeDumps/blob/master/InfoJson/2022.3.5f1.json). Its source URL and SHA-256 are included inside the payload. This is not a copy of the Unity engine or its implementation.

HOK renderer fields (`m_GIType`, `m_UseShadowMask` and the Yarp lightmap properties) are adapted from this project's existing HOK reader. The selected layouts were checked against complete object boundaries in the fixed local corpus. Only class IDs and original type hashes that passed are included. A matching version alone is insufficient.

The decoder validates every new object again. A matching schema does not imply that custom shaders, scripts or particle behavior have been reproduced in the preview renderer. This data is attributed separately; the project's MIT license must not be represented as a license grant from Unity or the upstream archive.
