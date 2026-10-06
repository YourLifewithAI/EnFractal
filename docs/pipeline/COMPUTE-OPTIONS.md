# Compute options for the capture pipeline, costed

**Prices checked 6 October 2026** from provider pricing pages and current comparison posts. They change often; re-check a page before committing to it. The founder's constraint: keep extra spend minimal, a few dollars at a time is acceptable, no new subscriptions unless clearly worth it.

## What one room costs in work

Estimates for the garage test case: about 150 photos at 24 MP, one bounded room, 25–40 full object assets plus 8–12 clutter sets.

| Stage | Work | Fits the RTX 2070 Super (8 GB)? |
|---|---|---|
| Ingest, blur/exposure scoring, HEIC conversion | CPU, minutes | Yes |
| Feed-forward poses (VGGT / MASt3R) | GPU, seconds per batch | Yes in batches of roughly 25–40 frames at reduced resolution (VGGT-class models need ~8 GB at 25 frames and ~10 GB at 50 frames of 624×416); the skill chunks and merges |
| Open-vocabulary detection and SAM 2 masks | GPU, minutes | Yes |
| Reference 3DGS of the room (gsplat) | GPU, 20–60 min at reduced resolution | Yes; room-sized scenes need well under 8 GB |
| Plane fitting / layout | CPU | Yes |
| Per-object image-to-3D, 35–50 generations | GPU, 0.5–3 min each | Partly: see the model table |
| Texture restyle per object | GPU, 1–3 min each | Yes for SD-class image-to-image at 1024 px |
| Headless Godot review renders | GPU/CPU | Yes |

So the only stage that does not fit comfortably on the local card is **object generation with textures**. Everything else is free apart from electricity and time.

## Image-to-3D models on the local card

| Model | VRAM reported | Verdict for 8 GB |
|---|---|---|
| Hunyuan3D 2.1 | ~10 GB shape only, ~21 GB with texture; a community "2GP" offload fork runs shape on 6 GB | Shape locally with the fork; textures not locally |
| TRELLIS (original) | 16 GB recommended | No |
| TRELLIS.2-4B | 6–8 GB in low-VRAM mode per community guides, 16 GB for comfortable 512³ | Possible at reduced settings; verify on the actual card |
| SAM 3D Objects (Meta, Nov 2025) | 16 GB+, 24 GB for multi-object scenes; custom SAM license | No locally; the most relevant model for "object in a photo to posed asset", so worth a hosted or rented-GPU run |
| Stable Point Aware 3D (SPAR3D) | Designed for consumer RTX cards | Yes; lower fidelity, fast, good for clutter tier |
| TripoSR / Stable Fast 3D | Small | Yes; lowest fidelity |

## Option A: pay per object through a hosted API (no subscription)

Prices per generation, from the providers' pages unless noted.

| Provider and model | Price | Notes |
|---|---|---|
| fal.ai, TRELLIS | $0.02 | Usage-based, no subscription |
| fal.ai, Hunyuan3D 2.1 | $0.05 | Listed on the Hunyuan v2 page comparison |
| fal.ai, Hunyuan3D v2 | $0.16 white mesh, $0.48 textured | Multi-view variants same base price |
| fal.ai, Hunyuan3D v3.1 Pro | ~$0.375 (+$0.15 PBR, +$0.15 multi-view) | From comparison posts; confirm on the model page |
| Replicate, TRELLIS | ~$0.036 per run (~26 s on an A100 80 GB) | Per-second billing of the underlying GPU |
| Stability AI, SPAR3D | 4 credits = $0.04 | Credit = $0.01; 25 free credits on signup per comparison posts |
| Tripo API | 20 credits mesh only, 30 with texture; 1 credit = $0.01 | $0.20–0.30 per object |
| Meshy API | 20 credits mesh only, 30 with 2K/4K texture, 35 with 8K (meshy-7.1); 5–20 credits on lite models | Credits bought via a subscription; Pro is $20/month for 1,000 credits, roughly 33–50 textured objects |
| Rodin (Hyper3D) | Creator $30/month (~60 models); download-to-pay on the free tier | Subscription-shaped |

**Per garage at 45 generations:**

| Choice | Cost per room |
|---|---|
| fal.ai TRELLIS | ≈ $0.90 |
| fal.ai Hunyuan3D 2.1 | ≈ $2.25 |
| Replicate TRELLIS | ≈ $1.60 |
| Stability SPAR3D | ≈ $1.80 |
| fal.ai Hunyuan3D v2 textured | ≈ $21.60 |
| Tripo textured | ≈ $13.50 |
| Meshy Pro | $20/month, covers roughly one room per month |
| Hunyuan3D v3.1 Pro with PBR | ≈ $24 |

Licensing note: Tripo's free plan publishes models under CC BY 4.0 without commercial rights; paid plans and the API unlock private commercial use. Meshy's and Rodin's terms differ by plan. Hunyuan3D weights carry Tencent's community license with regional restrictions. SAM 3D Objects uses Meta's custom SAM license. Check each before a shared-room or business use case.

## Option B: rent a GPU by the hour and run everything yourself

| Provider | GPU | Price | Notes |
|---|---|---|---|
| RunPod Community Cloud | RTX 4090 (24 GB) | $0.34/hr | Per-second billing; Secure Cloud $0.69/hr |
| RunPod Community Cloud | A100 80 GB | $1.19/hr | |
| Vast.ai | RTX 4090 | ~$0.29–0.59/hr on demand, spot from ~$0.14/hr | Marketplace; reliability varies by host |
| Modal | A100 40 GB / 80 GB | ~$2.10 / $2.50 per hour equivalent, billed per second | **$30/month free credit on every account**, roughly a dozen A100 hours |
| Google Colab Pro | A100 40 GB via compute units | $9.99/month for 100 units; A100 ≈ 5.4 units/hr (~18 hrs) | Notebook-shaped; GPU availability not guaranteed |
| Lambda | A100 40 GB | $1.99/hr | H100s sold as 8-GPU nodes; overkill here |

**Per garage:** 2–3 GPU-hours covers the reference splat, all object generations with Hunyuan3D 2.1 or TRELLIS.2 at full settings, and the texture restyle.

| Choice | Cost per room |
|---|---|
| RunPod 4090 | ≈ $0.70–1.00 |
| Vast.ai 4090 spot | ≈ $0.30–0.50 (interruptible) |
| Modal A100 within the free credit | $0 for the first ~12 hours each month |
| Colab Pro | ≈ $1.50 of a $9.99 month |

Rented GPUs also unlock the models the local card cannot run (SAM 3D Objects, Hunyuan textures, full-resolution TRELLIS), and keep the pipeline code identical to the local path, which matters for an MCP server other players will run.

## Option C: everything local

$0 per room. Object generation limited to shape-only Hunyuan3D 2.1 (offload fork), TRELLIS.2 in low-VRAM mode if it proves stable on the card, SPAR3D and TripoSR. Textures come from the style pass rather than the generator, which for a painterly preset is less of a loss than it sounds. Slower: expect an afternoon of unattended GPU time per room.

## Recommendation

1. **Default to local for every stage except object generation.** It is free and it is what any player with a gaming PC will run.
2. **Object generation through fal.ai per object**, TRELLIS or Hunyuan3D 2.1 for the bulk of objects and a higher-quality backend only for a few hero objects. Budget **$1–3 per room**. Put a spend cap on the account.
3. **Use Modal's $30 monthly free credit** for the jobs that need more than 8 GB: SAM 3D Objects trials, textured Hunyuan runs, higher-resolution reference splats. Fall back to RunPod Community 4090 at $0.34/hr if the credit runs out.
4. **No new subscriptions** (Meshy, Tripo, Rodin, Colab) until per-object spend consistently exceeds $20/month, which at 2–3 test rooms a month it will not.

Expected spend while testing two or three rooms a month: **under $10**, most months near $3.

## The agent's own inference

When the capture skill runs inside a Claude Code or Claude desktop session on the founder's existing plan, the vision review of photos and the inventory review cost nothing extra. Run through a metered API, each coverage pass over 150 photos is a few hundred image tokens per photo plus text; measure one pass before assuming it is cheap, and cache per-photo assessments so a re-run only looks at new photos.

## Sources

- RunPod pricing breakdown: https://northflank.com/blog/runpod-gpu-pricing and https://www.synpixcloud.com/blog/rtx-4090-cloud-rental-worth-it
- Vast.ai RTX 4090 pricing: https://www.synpixcloud.com/blog/vast-ai-vs-runpod-rtx-4090-pricing and https://aliteq.com/cheapest-rtx-4090-rental-2026
- Modal pricing and free credit: https://costbench.com/software/ai-gpu-cloud/modal/ and https://yangmao.ai/en/compute/modal/
- Lambda pricing: https://www.synpixcloud.com/blog/lambda-labs-gpu-pricing-2026
- Colab compute units: https://www.aquanode.io/vs/google-colab
- fal.ai TRELLIS: https://fal.ai/models/fal-ai/trellis ; Hunyuan3D v2: https://fal.ai/models/fal-ai/hunyuan3d/v2 ; pricing index: https://fal.ai/pricing
- Replicate TRELLIS: https://replicate.com/firtoz/trellis
- Meshy API credits: https://docs.meshy.ai/en/api/pricing ; plans: https://docs.meshy.ai/en/webapp/pricing
- Tripo API credits: https://developers.tripo3d.ai/en/pricing
- Stability AI pricing: https://platform.stability.ai/pricing (SPAR3D 4 credits per comparison posts: https://developer.puter.com/tutorials/stability-ai-api-pricing/)
- Rodin plans: https://www.therundown.ai/tools/rodin
- Hunyuan3D 2.1 VRAM: https://codersera.com/blog/set-up-hunyuan3d-2-on-windows-a-step-by-step-guide/amp/ and https://www.tspi.at/2026/05/01/opensource3dassets.html
- TRELLIS.2 low-VRAM guidance: https://trellis2.app/blog/trellis-2-4b
- SAM 3D Objects: https://huggingface.co/facebook/sam-3d-objects and https://roboflow.com/model/sam-3d-objects
- VGGT memory by frame count: https://github.com/facebookresearch/vggt-omega
- 3DGS room-scene memory: https://arxiv.org/html/2406.17074v1
