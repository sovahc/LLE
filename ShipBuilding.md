# Ship Building by LLM — design notes

Status: exploration, nothing implemented. Summary of a research pass (2026-07-29).
Claims marked **[verified]** were read in the SE source or measured on disk.
Everything else is literature relayed via GLM web search — treat as hypothesis until the paper is opened.

---

## Goal

Not generating ships from scratch — the workshop already has hundreds of thousands.
The target is **instructed editing**: tell the model by text or voice "add gyroscopes",
and it understands the principle and places them.

---

## Game facts that shape the problem

**[verified]** Gyroscope torque is position-independent.
`MyGridGyroSystem.cs:386` sums `m_maxGyroForce += gyro.MaxGyroForce` — a scalar over all
gyros, no position term. Placement only has to fit and be powered.

**[verified]** Thrust is applied at the center of mass, so thruster placement creates no torque.
`MyThrusterBlockThrustComponent.cs:52`:
`CubeGrid.Physics.AddForce(ADD_BODY_FORCE_AND_BODY_TORQUE, thrust, null, null)` — the third
argument is the application point, and it is `null`. (A mod exists that re-enables CoM effects;
default game does not.)

Consequence: **most useful edits need almost no geometry.**

| Edit class | What it actually needs |
|---|---|
| Gyros, reactors, batteries, cargo | Free interior volume + power. No geometry. |
| Thrusters | Direction coverage (thrust per axis) + clear exhaust behind the nozzle. Both local. |
| Armor, hull plating | Surface topology. This one does need a map. |

So the representation should be per-task, not one-size-fits-all. A compact summary
(block inventory by type and orientation, bounding box, mass, thrust per axis, free volume)
covers the first two classes in a few hundred characters.

---

## Training data — solved by removal

No human build traces exist for SE ships, but finished ships are abundant.
**Remove blocks by category from a finished blueprint**: the stripped ship is the input,
the removed blocks are the exact ground truth. A partially stripped ship is also a legitimate
game state (a ship under construction), not an artificial corruption.

This sidesteps the BIFI warning about synthetic corruption distributions — here the corruption
*is* the task distribution.

It also gives a **free auto-benchmark**: strip → ask to restore → compare against the human
original. Tens of thousands of instances, zero labeling, runs on a non-fine-tuned model today.

Blueprint format: `.sbc` XML, `<MyObjectBuilder_CubeBlock>` with `SubtypeName`, `Min x/y/z`,
`BlockOrientation Forward/Up`. Everything else (EntityId, color, owner, HUD flags) is noise.
**[verified]** Measured on 67 local workshop blueprints: 421,500 blocks, ~1 KB of XML per block;
stripped to essentials the whole set is ~17 MB.

---

## What the literature says about the representation

**Text beats images for grids.** BALROG: GPT-4o 32.3% text-only vs 22.6% when also shown an
image of the same scene; Llama 3.2 90B 27.3% → 21.0%. Grid renders are out-of-distribution for
VLMs. Note the boundary: this is about *symbol maps*. Natural-looking game renders are in
distribution — Gemma read a cargo container with blocked ports off a screenshot fine.

**Do not have the model emit raw cell grids.** The whole field routes around it:
- MarioGPT (the one 2D success): fine-tuned **DistilGPT2, 96M params**, 88.4% playable vs 31%
  for LSTM — but each tile got its **own token** and levels were flattened column-major.
  It deliberately does not emit ASCII grids.
- 3D work (T2BM, Text2BIM, 3D-GPT, SceneCraft, Text-to-CadQuery) emits **code or layer-JSON**;
  the structure is built downstream.
- Where voxels really are placed block-by-block (3D-Craft / VoxelCNN, 2,500 Minecraft houses,
  local 7×7×7 context) the generator is a **CNN, not an LLM**.
- Reason: subword tokenization makes exact N×M emission unreliable, and a flattened grid forces
  2D reconstruction from a linear token stream.
- There is **no established way** to feed a 3D grid to an LLM. LoST / pts3d-llm target point
  clouds and meshes, not cell grids.

**This warning applies to output volume, not to us — if the output is a diff.**
"Add gyroscopes" is four coordinates, not a 20×10×30 grid. So: **train on the difference,
never on the whole repaired ship.** Pairs shaped as `stripped ship + task → list of placements`
stay out of the trap; `broken ship → full repaired ship` walks straight into it.

**Invented DSLs are worse than they look.** Models hallucinate non-existent functions in unseen
DSLs (LLMLift). Same model, same task, changing only the language spans ~32%→90% pass rate by
how well-represented the language is (McEval). Switching a *prompt* to a lower-resource language
costs 3–4 grid sizes on spatial tasks (MazeEval). The documented winning pattern everywhere is:
**emit a familiar language, translate to the target mechanically in code.**

**Functional validity comes from a validator, not the generator.** Every working PCG pipeline
gets it from a checker in the loop (playability ~95% only *after* repair). A shape prior produces
ships that look like ships and do not fly. SE is unusually well suited here: thrust per axis,
power budget, gyro count, conveyor connectivity are all cheap arithmetic.

---

## Fine-tuning notes

- LoRA at r=16–64 **substantially underperforms** full fine-tuning. Matching it needs
  **r=256, α=2r, all modules including MLP** (LoRA Learns Less and Forgets Less, Biderman et al.).
  Corroboration: Text-to-CadQuery's Mistral-7B LoRA r=16 lost to a fully fine-tuned 3B.
- Forgetting is the trade-off, not the objection: general benchmarks after code tuning were
  0.509 (LoRA r=64) vs 0.414 (full FT). LoRA is the less destructive point on the curve.
- Text-to-CadQuery: 170K pairs, best result at **Qwen2.5-3B** (69.3% exact match); 7B underfit.
  Small models are the demonstrated regime here — scale is not the lever.
- Build order is a real lever with measured effects. No direct ablation for VoxelCNN, but in
  autoregressive image generation a fixed raster order is consistently suboptimal and
  **random-order training beats raster** (RandAR, CVPR 2025). A synthesized bottom-up order will
  work but is unlikely to be optimal.
- Cheap trick from MarioGPT, reproducible without a custom tokenizer: run Gemma's tokenizer over
  candidate block symbols and keep only those that encode to a single token.

---

## Open decisions

1. **Benchmark first, or train first.** Recommended: build the strip-and-restore harness and
   measure prompted Gemma with no training. MarioGPT solved its task at 96M parameters; a 31B
   may already be somewhere decent, and that floor has to be known before LoRA can be judged.
2. Which edit class to target first — geometry-free (gyros, power, cargo) or thrusters.
3. Whether to build the functional validator (thrust/power/gyro/conveyor) up front. It is the
   BIFI critic, the inference-time gate, and useful in every other variant too.

Visual feedback (screenshot on model request) is deferred — useful as an evaluator for
diagnosing a specific problem spot, not as the generator's input channel.
