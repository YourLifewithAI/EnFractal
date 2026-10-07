# Brief 03: image-to-3D survey for the five-object pilot

Research checked **7 October 2026**. Branch: `codex/03-image-to-3d-survey`. Research only; no weights downloaded, installations, generation jobs, GPU use, paid API calls, commits or pushes. Spend: **$0**; GPU time: **0**. Read the named brief, project rules, ownership map, contracts overview and both pipeline documents. The longer report is needed to cover the brief's per-candidate evidence requirements.

## Recommendation

Pilot **SPAR3D locally, TRELLIS.2 through a hosted endpoint, and SAM 3D Objects through a hosted endpoint**. These compare a plausible 8 GB workflow, detailed PBR generation, and reconstruction of masked objects in natural scenes. This is a research recommendation, not a measured quality ranking. All three must face the same five objects and the founder's review. See the linked evidence and qualifications below.

Use **TripoSR as the permissively licensed local fallback** if the founder declines Stability's terms. Do not inherit an Apache-2.0-only rule from Lane C's earlier choice: the brief sets no such rule, and MIT permits commercial use too. Code, generator weights, auxiliary weights and service output rights are separate checks; the [TripoSR license](https://github.com/VAST-AI-Research/TripoSR/blob/main/LICENSE) and [weight card](https://huggingface.co/stabilityai/TripoSR) establish its MIT status.

## Reading the evidence

Links point to primary repositories, model cards, papers and provider documentation, checked during this task. Published benchmarks are author/vendor reports, not measurements here. **Unverified** means the requested fact was not established by the inspected sources. Source URLs using `main` are mutable; the implementation lane should pin revisions before a trial.

The memory numbers below are documented operating requirements or reported consumption, **not experimentally established minimums**. “Fits 8 GB” means the documentation suggests a viable configuration; successful installation, peak memory and speed on Windows/RTX 2070 SUPER remain **unverified for every local candidate**. For a hosted service, client VRAM is irrelevant and backend VRAM is undisclosed/unverified. Multiple filenames passed to a batch script do not imply multi-view fusion.

## Local and open-weight candidates

### 1. TripoSR — original 2024 checkpoint, `stabilityai/TripoSR`

- **Official / licenses:** [official repository](https://github.com/VAST-AI-Research/TripoSR). Code: [MIT](https://github.com/VAST-AI-Research/TripoSR/blob/main/LICENSE). Weights: [MIT model card](https://huggingface.co/stabilityai/TripoSR). Neither is research-only or non-commercial.
- **Memory / Windows:** [README](https://raw.githubusercontent.com/VAST-AI-Research/TripoSR/main/README.md) reports about **6 GB** with default single-image settings: plausible in 8 GB. Windows-specific support guarantee: **unverified**; Python/PyTorch/CUDA instructions are supplied, and the README documents `torchmcubes` compilation trouble.
- **Inputs / outputs:** One object image; background removal/cropping precedes inference. Independent images can be batched; multi-view fusion: **unverified**. Mesh with vertex colors or optional baked texture (`--bake-texture`); no PBR material-map output verified. Fixed/default polycount: **unverified**, extraction-dependent. [README](https://raw.githubusercontent.com/VAST-AI-Research/TripoSR/main/README.md).
- **Time:** Author reports **<0.5 s on A100**, which is a datacenter GPU; typical consumer GPU and 2070 SUPER end-to-end time: **unverified**. [README](https://raw.githubusercontent.com/VAST-AI-Research/TripoSR/main/README.md).
- **Weak spots:** Later SF3D research documents marching-cubes artifacts and lighting baked into predecessor outputs. For boxes, bins and shelving, rounded corners, ambiguous backs and missing thin parts are pilot risks (**inference; object-specific incidence unverified**). [SF3D paper](https://arxiv.org/html/2408.00653v1).
- **Hosted:** [official Hugging Face demo](https://huggingface.co/spaces/stabilityai/TripoSR); guaranteed availability, API tariff and quota: **unverified**. Do not conflate this checkpoint with Tripo's commercial service models.

### 2. Stable Fast 3D (SF3D) — original 2024 checkpoint

- **Official / licenses:** [repository](https://github.com/Stability-AI/stable-fast-3d). Code: [Stability AI Community License](https://github.com/Stability-AI/stable-fast-3d/blob/main/LICENSE.md). Weights: [same license on model card](https://huggingface.co/stabilityai/stable-fast-3d). Conditional commercial use, not an unrestricted MIT/Apache license; registration, revenue and attribution terms apply. See license notes below.
- **Memory / Windows:** About **6 GB** for default single-image inference, hence plausible in 8 GB. Native Windows support is explicitly **experimental**, requires VS 2022 and matching PyTorch/CUDA. [README](https://raw.githubusercontent.com/Stability-AI/stable-fast-3d/main/README.md).
- **Inputs / outputs:** One image; no verified multi-view fusion. UV-unwrapped textured **GLB**, configurable texture resolution and optional triangle/quad remeshing. [README](https://raw.githubusercontent.com/Stability-AI/stable-fast-3d/main/README.md). Albedo, normal mapping, and predicted **homogeneous per-object metallic/roughness**, rather than independently varying material maps; one reported example is **27.4k triangles**, not a fixed output count. [Paper](https://arxiv.org/html/2408.00653v1).
- **Time:** **0.5 s on H100** in the paper; [launch page](https://stability.ai/news-updates/introducing-stable-fast-3d) also advertises 0.5 s with a 7 GB GPU without naming the GPU. Typical consumer and 2070 SUPER timings: **unverified**.
- **Weak spots:** Paper identifies dark input regions with lost information and inability to represent strongly different materials with homogeneous metallic/roughness. This matters for a plastic toolbox with metal hardware (**application inference**). [Paper](https://arxiv.org/html/2408.00653v1).
- **Hosted:** Stability API and [HF demo](https://huggingface.co/spaces/stabilityai/stable-fast-3d) are linked by the [launch page](https://stability.ai/news-updates/introducing-stable-fast-3d). Current API price/availability: **unverified**; the [pricing page](https://platform.stability.ai/pricing) returned no readable tariff.

### 3. Stable Point-Aware 3D (SPAR3D) — original 2025 checkpoint

- **Official / licenses:** [repository](https://github.com/Stability-AI/stable-point-aware-3d). Code: [Stability AI Community License](https://github.com/Stability-AI/stable-point-aware-3d/blob/main/LICENSE.md). Weights: [Community License model card](https://huggingface.co/stabilityai/stable-point-aware-3d). Conditional commercial use; not research-only, not Apache-2.0.
- **Memory / Windows:** README's specific low-VRAM section reports **10.5 GB normally, roughly 7 GB with `SPAR3D_LOW_VRAM=1` / `--low-vram-mode`**, at slower speed. Its later inference paragraph also says 6 GB, an internal contradiction. Plan around **7 GB low-VRAM**, with actual 8 GB success **unverified**. Windows is explicitly experimental with VS 2022. [README](https://raw.githubusercontent.com/Stability-AI/stable-point-aware-3d/main/README.md).
- **Inputs / outputs:** Single image (model expects 512×512) plus generated/editable point cloud; no verified multi-view fusion. UV-textured **GLB** and editable point representation. [Model card](https://huggingface.co/stabilityai/stable-point-aware-3d). Paper describes albedo, normals and metallic/roughness material prediction; independently varying metallic/roughness maps are **unverified**. [Paper](https://arxiv.org/html/2501.04689v1). Remesh count is a rough target, not an exact guarantee; fixed polycount **unverified**. [README](https://raw.githubusercontent.com/Stability-AI/stable-point-aware-3d/main/README.md).
- **Time:** Paper reports **0.7 s**, but a verified consumer GPU/time pairing and low-VRAM runtime are **unverified**. [Paper](https://arxiv.org/html/2501.04689v1).
- **Weak spots:** Paper acknowledges spikes/detached parts and imperfect material decomposition. Handle fidelity and flat-panel quality on these five objects: **unverified**; inspect both. [Paper](https://arxiv.org/html/2501.04689v1).
- **Hosted:** [official HF demo](https://huggingface.co/spaces/stabilityai/stable-point-aware-3d). The pipeline's Stability **$0.04** claim is **unverified** from the unreadable [current pricing page](https://platform.stability.ai/pricing); do not budget it as confirmed.

### 4. TRELLIS — original `TRELLIS-image-large` (2024/2025)

- **Official / licenses:** [repository](https://github.com/microsoft/TRELLIS). Code: [MIT](https://github.com/microsoft/TRELLIS/blob/main/LICENSE). Weights: [MIT](https://huggingface.co/microsoft/TRELLIS-image-large). Core licenses are commercial-compatible; auxiliary dependencies need their own review.
- **Memory / Windows:** Official requirement **at least 16 GB**: no documented stock 8 GB path. Tested Linux; README links Windows community setup, explicitly not fully tested. [README](https://raw.githubusercontent.com/microsoft/TRELLIS/main/README.md).
- **Inputs / outputs:** One image; multiple images supported through a **tuning-free conditioning algorithm**, with an explicit warning that results may vary. Meshes, Gaussian splats and radiance fields; textured GLB export. Complete metallic/roughness/normal PBR-map suite and fixed polycount: **unverified**. [README](https://raw.githubusercontent.com/microsoft/TRELLIS/main/README.md).
- **Time:** Typical consumer GPU/2070 SUPER run time: **unverified** in inspected documentation; do not substitute hosted queue time.
- **Weak spots:** Multi-image conditioning has documented limitations. Full visibility and clear lighting are recommended by the [provider](https://fal.ai/models/fal-ai/trellis). Thin shelf members and plain-box backside accuracy remain **unverified**; generated unseen dimensions should be checked against measurements (**inference**).
- **Hosted:** [fal single-image endpoint](https://fal.ai/models/fal-ai/trellis), **$0.02/generation**. Five single-image generations: **$0.10**, arithmetic before retries. This is original TRELLIS, not TRELLIS.2; exact deployed weight revision and multi-view tariff are **unverified**.

### 5. TRELLIS.2 — `TRELLIS.2-4B` (December 2025)

- **Official / licenses:** [repository](https://github.com/microsoft/TRELLIS.2). Code: [MIT](https://github.com/microsoft/TRELLIS.2/blob/main/LICENSE). Weights: [MIT model card](https://huggingface.co/microsoft/TRELLIS.2-4B). This does not license auxiliary models automatically; see dependency notes.
- **Memory / Windows:** Official requirement **at least 24 GB**, tested Linux/A100/H100. **No verified stock 8 GB support.** Community low-VRAM and Windows claims are **unverified for this card/configuration**. [Requirements](https://github.com/microsoft/TRELLIS.2#installation).
- **Inputs / outputs:** Single image; multi-view fusion **unverified** in official release. PBR **GLB**, base color, roughness, metallic and opacity. Example export uses **1,000,000-face decimation target / 4096 texture**, configurable rather than fixed. Voxel resolutions 512³–1536³ are not polygon counts. [README](https://github.com/microsoft/TRELLIS.2).
- **Time:** **3 / 17 / 60 s at 512³ / 1024³ / 1536³ on H100**. Typical consumer GPU and 2070 SUPER times: **unverified**. [Model card](https://huggingface.co/microsoft/TRELLIS.2-4B).
- **Weak spots:** Official card acknowledges **small holes/topological discontinuities** and unaligned aesthetic preferences. Watertight output is not guaranteed. For plain household objects, panel warping and handle loss remain **unverified pilot risks**. [Model card](https://huggingface.co/microsoft/TRELLIS.2-4B).
- **Hosted:** [fal](https://fal.ai/models/fal-ai/trellis-2): **$0.25 / $0.30 / $0.35** for the three resolution tiers; [schema](https://fal.ai/models/fal-ai/trellis-2/api) exposes texture and decimation settings. Five 1024-tier runs: **$1.50**, arithmetic before retries; hosted latency **unverified**.

### 6. Hunyuan3D 2.1 — Shape-v2-1 + Paint-v2-1 (June 2025)

- **Official / licenses:** [repository](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1). Code: [Tencent Hunyuan 3D 2.1 Community License](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/blob/main/LICENSE). Weights: [same license in weight repository](https://huggingface.co/tencent/Hunyuan3D-2.1/blob/main/LICENSE). Commercial use is conditional, not blanket non-commercial; territorial restrictions include outputs. Details below.
- **Memory / Windows:** Official README reports **10 GB shape, 21 GB texture, 29 GB total shape+texture**. Not a stock 8 GB candidate. It advertises Windows/macOS/Linux and a low-VRAM flag; an actual 8 GB configuration and native Windows build on the founder's card are **unverified**. [README](https://raw.githubusercontent.com/Tencent-Hunyuan/Hunyuan3D-2.1/main/README.md).
- **Inputs / outputs:** Shape from one image, then mesh plus reference image for painting. Verified multiple observed-view shape conditioning: **unverified for 2.1**; do not import the separate 2.0-mv model's capability. Textured GLB with PBR; [paint documentation](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/blob/main/hy3dpaint/README.md) distinguishes generated painting views from input photos. Exact emitted map inventory, default polycount: **unverified**.
- **Time:** Typical consumer GPU shape/paint and 2070 SUPER times: **unverified**; no runtime extrapolation from model parameter counts.
- **Weak spots:** Five-object household accuracy, closure, straight edges, thin supports and unseen bin interiors: **unverified**. Treat image-conditioned generation as an asset proposal requiring inspection (**inference**).
- **Hosted:** [official HF demo](https://huggingface.co/spaces/tencent/Hunyuan3D-2.1), quota/availability **unverified**. [fal 2.1 endpoint](https://fal.ai/models/fal-ai/hunyuan3d-v21) is **deprecated/no longer supported**, although it displays $0.30. The pipeline's $0.05 claim is not a verified usable offer.

### 7. SAM 3D Objects — November 2025 checkpoints; encoder release June 2026

- **Official / licenses:** [repository](https://github.com/facebookresearch/sam-3d-objects). Code: [SAM License](https://github.com/facebookresearch/sam-3d-objects/blob/main/LICENSE). Weights: [model card](https://huggingface.co/facebook/sam-3d-objects) and [README's explicit code/checkpoint license statement](https://raw.githubusercontent.com/facebookresearch/sam-3d-objects/main/README.md). Custom commercial-compatible grant with use/redistribution restrictions; not SAM 2's Apache license, not research-only.
- **Memory / Windows:** Official setup requires **Linux 64-bit and at least 32 GB VRAM**. No supported 8 GB or native Windows path established. The earlier pipeline's 16 GB+ figure is not the current official requirement. [Setup](https://github.com/facebookresearch/sam-3d-objects/blob/main/doc/setup.md).
- **Inputs / outputs:** One scene image plus object mask(s); one or several objects **from the same image**, not several camera views. Reconstructed shape/texture/layout and pose, with Gaussian PLY export. [README](https://raw.githubusercontent.com/facebookresearch/sam-3d-objects/main/README.md). Hosted adapter also exposes individual **GLBs and transform metadata**. [fal schema](https://fal.ai/models/fal-ai/sam-3/3d-objects/api). Complete PBR maps and fixed polygon count: **unverified**.
- **Time:** Typical consumer GPU and 2070 SUPER runtimes: **unverified**.
- **Weak spots:** Authors target clutter/occlusion/small objects; this is evidence of intended suitability, not proof of garage accuracy. Bin wall thickness, shelf gaps and metric scale remain **unverified**. [README](https://raw.githubusercontent.com/facebookresearch/sam-3d-objects/main/README.md).
- **Hosted:** [Meta demo](https://www.aidemos.meta.com/segment-anything/editor/convert-image-to-3d), tariff/quotas **unverified**. [fal endpoint](https://fal.ai/models/fal-ai/sam-3/3d-objects) displays **$0.02/unit** and describes $0.02/reconstruction. Budget **$0.10 for five separate single-object requests**, conditional on that unit interpretation; multi-mask billing and latency **unverified**.

## Current hosted alternatives

For these services, public generator **code license and weight license are separately unverified**; API access is not an open-weight license. Local VRAM minimum and consumer inference time are unavailable/unverified. Windows can act as a browser/HTTP client (**inference from the documented API**); this says nothing about local inference support.

### 8. Hunyuan 3D v3.1 Pro — hosted service version

- **Official / licenses:** Tencent's [official site](https://3d.hunyuan.tencent.com/) is linked by its [2.1 repository](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1). Version-specific public code license: **unverified**; public weights/license: **unverified**. Do not reuse 2.1's license for 3.1. [fal's provider page](https://fal.ai/hunyuan-3d) explicitly advertises commercial output use, subject to service terms; global output-right details remain **unverified**.
- **Memory / Windows / time:** Hosted only in this survey: 8 GB client is sufficient (**API-client inference**), server VRAM/minimum **unverified**, local Windows support **unverified**, consumer run time not applicable; typical hosted latency **unverified**.
- **Inputs / outputs:** Front image plus up to seven named views (back, left, right, top, bottom, two diagonals); no explicit mask input verified. GLB/OBJ, textured or geometry-only, optional metallic/roughness/normal PBR textures. Target **40k–1.5m faces**, default **500k**; not game-budget geometry by default. [API schema](https://fal.ai/models/fal-ai/hunyuan-3d/v3.1/pro/image-to-3d/api).
- **Weak spots:** Requests call for one clear object on a simple background filling >50% of frame. Fidelity of hollow bins, shelves and plain panels: **unverified**; compare observed views before and after generation. [Schema](https://fal.ai/models/fal-ai/hunyuan-3d/v3.1/pro/image-to-3d/api).
- **Price:** [endpoint](https://fal.ai/models/fal-ai/hunyuan-3d/v3.1/pro/image-to-3d): **$0.375 base**, **+$0.15 PBR**, **+$0.15 multi-view**, **+$0.15 custom face count**. Five multi-view+PBR runs with default faces: **$3.375**; with custom face count: **$4.125**, arithmetic before retries. New endpoint schema says LowPoly is unavailable; do not rely on older v3 pricing descriptions.

### 9. Meshy 7.1 — `meshy-7.1`; Meshy T2 as topology variant

- **Official / licenses:** [official API](https://www.meshy.ai/api) and [current model IDs](https://docs.meshy.ai/en/api/image-to-3d). Code license: **unverified**, weights license: **unverified**; neither verified as downloadable. Output terms: [Free CC BY 4.0 with attribution; paid plans private commercial use](https://docs.meshy.ai/en/webapp/pricing). These are output terms, not generator licenses.
- **Memory / Windows / time:** Hosted: client VRAM irrelevant, server minimum **unverified**, local 8 GB/native Windows inference **unverified**. Typical consumer time not applicable; [web guide](https://docs.meshy.ai/en/webapp/image-to-3d) reports **about 1–2 minutes for Meshy 7**, not a verified 7.1 SLA.
- **Inputs / outputs:** One image or [1–4 views](https://docs.meshy.ai/en/api/multi-image-to-3d); dedicated mask argument **unverified**. GLB/FBX/OBJ/USDZ/STL; optional base color, metallic, roughness, normal maps. Remesh target **100–300k faces**, default **30k**; T2 generates **100–15k**, default **4k**, and lacks multi-image support in the pricing table. [Schema](https://docs.meshy.ai/en/api/image-to-3d), [pricing](https://docs.meshy.ai/en/api/pricing).
- **Weak spots:** Household-object comparative quality **unverified**. Optional image enhancement may alter appearance; disable it when testing fidelity (**recommendation based on the [multi-image schema](https://docs.meshy.ai/en/api/multi-image-to-3d)**).
- **Price:** [API tariff](https://docs.meshy.ai/en/api/pricing): **20 credits mesh / 30 with 2K or 4K textures / 35 with 8K**, **+5 Ultra geometry**, same base costs for multi-image. T2: **5 mesh / 15 textured at 2K or 4K**. Credits are prepaid via account settings; current dollar conversion/minimum purchase **unverified**. Five textured 7.1 requests: **150 credits**, not automatically $3 or a $20 subscription. Web pricing has internally inconsistent Meshy 7 counts; use API tariff for API planning.

### 10. Tripo P1 — stable snapshot `P1-20260311`

- **Official / licenses:** [official model page](https://developers.tripo3d.ai/en/models/p1). Public code license: **unverified**; weight license: **unverified**, no local release established. [Developer terms](https://developers.tripo3d.ai/en/terms) describe broad paid-user output rights, subject to restrictions, including on competing services and exposing generation to third parties. Free web-plan rights must not be assumed identical to API rights.
- **Memory / Windows / time:** Hosted: backend VRAM minimum **unverified**, 8 GB client sufficient (**API-client inference**), local Windows/8 GB inference **unverified**. Typical consumer time not applicable. Vendor reports **~10 s geometry / ~60 s textured**, not independently measured. [Model page](https://developers.tripo3d.ai/en/models/p1).
- **Inputs / outputs:** Text, image or multiple views; mask input **unverified**. Low-poly mesh, texture/PBR/UV controls; `face_limit` parameter **48–20k**, while prose also says 50–20k (source inconsistency). Exact PBR map inventory **unverified**. [Model page](https://developers.tripo3d.ai/en/models/p1). GLB is shown by the [generation API](https://developers.tripo3d.ai/en/docs/generation-image-to-model/p).
- **Weak spots:** Thin bin rims and handles may be lost at very low face limits (**inference; unverified on pilot objects**). The API asks for visible subjects with minimal occlusion. [Generation API](https://developers.tripo3d.ai/en/docs/generation-image-to-model/p).
- **Price:** P1-specific page: **40 credits image/multi-view mesh, 50 standard textured, 60 detailed textured**. [Conversion](https://developers.tripo3d.ai/en/pricing): **1 credit = $0.01**. Therefore **$0.40 / $0.50 / $0.60**, five standard textured objects **$2.50**, arithmetic before retries. Use P1's model-specific table rather than the generic pricing page's initially visible H-series table. Newer P2 snapshot is referenced in the API; its complete requirements/terms/pricing were **unverified** because its model page did not return content, so no P2 ranking is made.

## License and dependency findings

- **Stability is not Apache/MIT.** Both SF3D and SPAR3D use the Community License for code and weights. The checked text requires registration for commercial use, provides free limited commercial use below the revenue threshold, and requires an enterprise license above $1m aggregate annual revenue. Redistribution/attribution and restrictions on training other foundational models also apply. The SPAR3D card has contradictory revenue wording; use the actual license agreement and obtain clarification at the threshold. [SPAR3D license](https://github.com/Stability-AI/stable-point-aware-3d/blob/main/LICENSE.md), [SF3D license](https://github.com/Stability-AI/stable-fast-3d/blob/main/LICENSE.md), [card](https://huggingface.co/stabilityai/stable-point-aware-3d).
- **Tencent 2.1 is conditional commercial, not simply non-commercial.** It excludes **EU, UK and South Korea**, restricts output use outside the territory, imposes notice/use conditions, and requires additional permission above the specified **1m monthly-active-user** threshold. This matters to eventual international distribution even if generation happens in Texas. [Full agreement](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/blob/main/LICENSE). The brief does not authorize adopting these terms.
- **Meta SAM License covers both code and weights.** Its grant is not limited to research, but redistribution must retain the agreement and use restrictions include specified military/weapons and trade-control uses. [Agreement](https://github.com/facebookresearch/sam-3d-objects/blob/main/LICENSE). Commercial suitability here is a reading of the grant, not legal clearance of every dependency.
- **TRELLIS.2 core MIT does not settle the complete pipeline.** Its README explicitly calls out separate NVIDIA renderer licenses. [README](https://github.com/microsoft/TRELLIS.2), [nvdiffrast terms](https://github.com/NVlabs/nvdiffrast/blob/main/LICENSE.txt). BRIA [RMBG-2.0 weights are CC BY-NC 4.0 unless commercially licensed](https://huggingface.co/briaai/RMBG-2.0), and [DINOv3 weights have Meta's separate license](https://huggingface.co/facebook/dinov3-vitl16-pretrain-lvd1689m). Whether each selected deployment loads those auxiliaries is **unverified in this survey**; inventory the exact pipeline before claiming all-MIT commercial deployment. An opaque hosted adapter's auxiliary licensing is also **unverified**; its commercial-use label is a provider representation.
- **TripoSG is a reserve, not an all-MIT shortcut.** Its [root MIT license](https://github.com/VAST-AI-Research/TripoSG/blob/main/LICENSE) coexists with a [NOTICE](https://github.com/VAST-AI-Research/TripoSG/blob/main/NOTICE) naming Tencent/BRIA components and a [nested FlashVDM Community License](https://github.com/VAST-AI-Research/TripoSG/blob/main/triposg/LICENSE). It reports [8 GB requirements](https://github.com/VAST-AI-Research/TripoSG), but a complete permissive license chain is **unverified**; it was not promoted over the ten fully profiled candidates.

## Pilot proposal and decisions for the integrator

This is proposed future work, not work performed in this research task.

| Pilot | Why it earns a slot | Proposed configuration and cost |
|---|---|---|
| SPAR3D | Only shortlisted higher-capability local candidate with an official roughly 7 GB offload path; editable backside representation may reduce repair work. Actual success unverified. | Low-VRAM mode, one isolated view, default texture settings; record Windows build and peak memory. $0 hosted spend. [README](https://github.com/Stability-AI/stable-point-aware-3d). |
| TRELLIS.2 | Test PBR and sharp geometry without treating stock inference as an 8 GB job. Core MIT terms are preferable to a territorial generator license, subject to auxiliary/service terms. | Hosted 1024 tier, preserve raw mesh, request a practical decimation target. Five runs $1.50 before retries. [Model](https://huggingface.co/microsoft/TRELLIS.2-4B), [endpoint](https://fal.ai/models/fal-ai/trellis-2). |
| SAM 3D Objects | Directly tests masked real-scene objects, clutter and pose/layout recovery; complements isolated-cutout generators. Intended strengths are not measured garage results. | Hosted one object/mask per request; verify GLB, metadata and billing unit. Five requests provisionally $0.10. [README](https://github.com/facebookresearch/sam-3d-objects), [endpoint](https://fal.ai/models/fal-ai/sam-3/3d-objects). |

Proposed first-pass hosted total: **$1.60**, arithmetic under the stated SAM unit assumption; approve a **$5 cap** to cover retries and any preparation charges. This task has spent nothing. If **strictly no hosted spending** is approved, substitute TripoSR for the hosted comparisons and report that full quality comparison remains unfinished; do not attempt unsupported local configurations to satisfy the plan.

If the pilot reveals wrong hidden geometry, the next comparison should be **Hunyuan 3D v3.1 Pro with actual multiple views** or **Tripo P1 with multiple views**, rather than only another single-image model. Costs and legal unknowns are in their profiles. This is a recommendation based on their documented inputs, not a claim that multi-view generation preserves measured geometry.

Suggested evaluation: same five objects, consistent crops/masks, measured bounding boxes, and held-out rear/underside views. Inspect straight panels, shelf gaps, thin handles, bin interiors, connected components, manifold/closure properties, texture lighting and polygon count before and after repair. Fit metric scale, create collision and apply the established style pipeline only in the Capture/Look lanes. Record preprocessing, model load, generation, texture/export, peak VRAM and repair time separately. Acceptance is the founder's judgment; no generation result certifies collision or usefulness at 10 cm avatar scale. This follows the [project pipeline](../../pipeline/ROOM-CAPTURE-PIPELINE.md).

**Decisions needed:** Stability license acceptance/registration if applicable; approval for future paid generations and founder-photo upload to a provider; acceptance of provider terms and storage/privacy behavior (not audited here). These do not block delivery of this research report. No contracts or other out-of-scope changes are required.

## Corrections to earlier pipeline assumptions

The following are evidence corrections for the integrator, not edits to the shared pipeline documents:

| Earlier assumption in supplied pipeline documents | Verified finding / disposition |
|---|---|
| TRELLIS.2 comfortable in 16 GB; 6–8 GB low-VRAM | Official release requires **24 GB**; smaller configurations remain unverified. [Requirements](https://github.com/microsoft/TRELLIS.2). |
| SAM 3D Objects 16 GB+ | Official setup requires **32 GB**. [Setup](https://github.com/facebookresearch/sam-3d-objects/blob/main/doc/setup.md). |
| Hunyuan 2.1 about 21 GB with textures | Official README distinguishes **21 GB paint / 29 GB combined**. [README](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1). |
| SPAR3D simply fits consumer RTX cards | Explicit **7 GB low-VRAM path**, conflicting 6 GB paragraph; actual 2070 SUPER outcome unverified. [README](https://github.com/Stability-AI/stable-point-aware-3d). |
| fal Hunyuan 2.1 $0.05/generation | Inspected 2.1 endpoint is **deprecated**, with a displayed historical $0.30 tariff. Do not use it as an active budget offer. [Endpoint](https://fal.ai/models/fal-ai/hunyuan3d-v21). |
| Meshy requires a $20 monthly subscription | Current API docs describe **prepaid credits**; dollar conversion/minimum purchase remain unverified. [API pricing](https://docs.meshy.ai/en/api/pricing). |

No shared-file change is necessary for this brief, so no out-of-scope patch is proposed.

## Limits and unfinished verification

No candidate was installed or run: the brief forbids generation/weights and this contractor cannot use the GPU or photos. Consequently local compatibility, end-to-end consumer timing, generated polycounts, game import, collision, household fidelity and comparative rankings remain **unverified**. Most inspected benchmarks use datacenter hardware. Unknown fields were retained explicitly rather than replaced with estimates.

Some public page reads returned `Internal Error`, including the initial Stability TripoSR GitHub URL, Tripo P2 model page and a guessed terms URL; no blocked download or paid operation was attempted. Canonical TripoSR and Tripo API terms were established through their official links. Stability's pricing page returned no readable body, leaving its tariffs unverified. The optional `docs/runs/RUN-2.md` read found no such file; the supplied named Brief 03 and pipeline context were sufficient, and no missing run file was created. Scope, setup and access guards were not altered.

Engine/contract runners were not executed: only this Markdown report is authorized, no contracts/data/code were produced, and GPU/runtime work is expressly excluded. Research limitations above require a separately authorized Capture-lane pilot, not more claims from this survey. Commit hashes: none; integrator reviews and commits the file.

## Local command evidence

Web research used read-only search/open/find tools; primary links beside claims are its evidence. All following shell commands ran in this checkout. Report content checks apply to the new file; `git diff --check` alone does not examine an untracked file.

```text
Command: git branch --show-current
Exit code: 0
codex/03-image-to-3d-survey

Command: git status --short
Exit code: 0
?? docs/codex/reports/03-image-to-3d-survey.md

Command: git diff --check
Exit code: 0
(no output; tracked changes only)
```

Content-check command, executed before adding this evidence block (the source-link count applies to that revision):

```powershell
$reportPath = 'C:\dev\EnFractal-codex\03-image-to-3d-survey\docs\codex\reports\03-image-to-3d-survey.md'
$reportText = [System.IO.File]::ReadAllText($reportPath)
$candidateCount = [regex]::Matches($reportText, '(?m)^### \d+\. ').Count
$linkCount = [regex]::Matches($reportText, '\]\(https://').Count
if ($candidateCount -ne 10) { throw "Expected 10 candidates; found $candidateCount" }
if ($reportText.Contains([char]13)) { throw 'Report contains CR line endings' }
if ($reportText -match 'turn\d+(search|view)\d+') { throw 'Report contains internal citation identifiers' }
Write-Output "PASS: $candidateCount candidate profiles"
Write-Output "PASS: $linkCount external Markdown source links"
Write-Output 'PASS: LF line endings; no internal citation identifiers'
```

```text
Exit code: 0
PASS: 10 candidate profiles
PASS: 111 external Markdown source links
PASS: LF line endings; no internal citation identifiers
```

Non-blocking context-read failure, preserved from the earlier compound read command (process exit code 0 because subsequent reads completed, not a successful RUN-2 read):

```text
Failing subcommand: Get-Content docs/runs/RUN-2.md -TotalCount 65
Get-Content : Cannot find path 'C:\dev\EnFractal-codex\03-image-to-3d-survey\docs\runs\RUN-2.md' because it does not
exist.
```

Final content/scope command, run after the evidence block above and before appending this transcript:

```powershell
$reportPath = 'C:\dev\EnFractal-codex\03-image-to-3d-survey\docs\codex\reports\03-image-to-3d-survey.md'
$reportText = [System.IO.File]::ReadAllText($reportPath)
if ([regex]::Matches($reportText, '(?m)^### \d+\. ').Count -ne 10) { throw 'Candidate count failed' }
if ($reportText.Contains([char]13)) { throw 'LF check failed' }
if ($reportText -match '(?m)[ \t]+$') { throw 'Trailing whitespace found' }
$changes = @(git status --short)
if ($changes.Count -ne 1 -or $changes[0] -ne '?? docs/codex/reports/03-image-to-3d-survey.md') { throw 'Scope check failed' }
Write-Output 'PASS: final report has 10 candidates, LF endings, no trailing whitespace; only scoped report changed'
```

```text
Exit code: 0
PASS: final report has 10 candidates, LF endings, no trailing whitespace; only scoped report changed
```
