# The Dead Wake - hatchet motion reference v1

- Date: 2026-09-24.
- Active motion reference: [hatchet-motion-v1.png](hatchet-motion-v1.png).
- Source/tool: built-in `image_gen.imagegen`, single reference-assisted generation.
- Actual saved dimensions: 1672 x 941 pixels, approximately 16:9. The tool returned this size despite the requested 2048 x 1152. No resizing or other image modification was applied.
- File size: 3,121,757 bytes.
- SHA-256: `E9F74FBCA78239B6F44FCCD5A04F3364656AFC9841CF87FC3C19D322C5220FCA`.
- Generated source: `C:/Users/Gray/.codex/generated_images/01a0d1b5-cca4-7ae0-8d1d-e6a427834e12/exec-19657085-6ec7-4d0f-8a23-fb8079e28efa.png`.
- Inputs opened before generation: [prototype-v1.png](prototype-v1.png) for accepted materials/art direction; [opening-crafted-v5.png](../../../Docs/ValidationEvidence/DeadWake/opening-crafted-v5.png) for the current compact hatchet and lower-right placement.
- Runtime asset paths: none. The storyboard is preserved outside runtime imports.
- Active version: v1; the original prototype concept remains active for overall art direction.

## Scope and defining traits

This four-panel AI-generated storyboard is an aspirational tool-pose reference, not captured gameplay and not proof of implemented animation, contact timing, resource handling, image quality, or performance. It is visibly labeled as a visual concept and not gameplay capture.

1. Ready: modest faceted stone head, timber shaft, three close cord wraps; tool rests at lower right with center aim clear.
2. Wind-up: short lift up/outward to the right; no resource award at button-down or preparation.
3. Contact: a short forward/down-left chop puts the cutting side against the fallen log; the small impact flecks and single `+2 WOOD` award appear at this pose.
4. Recovery: tool returns toward the lower-right ready position; no second award or new impact burst.

The fixed scene and repeated aim marker clarify that the tool moves while the view remains stable. Natural gray/offwhite stone, bark, cord, grass, and clear daylight retain the overall concept direction. No hand or arm is introduced as a new requirement; only the tool poses are shown. Guide arrows are storyboard annotations, not proposed runtime HUD.

## Inspection and limits

The actual generated media was saved, reopened, and inspected. All four labels and their captions are legible. The wind-up and contact poses are visibly distinct, the award appears only in the contact panel, and recovery is labeled without duplicate award. The image supplies useful pose and event-order intent.

The contact artwork places the visible blade impact slightly to the right of the crosshair; it must not be treated as a precise geometric specification. Runtime contact must align with the actual interaction target and swing representation. The contact head also changes apparent orientation/foreshortening; model identity and size must remain consistent in the animation. The detailed valley/settlement background comes from the broader concept and does not expand the current opening-slice scope.

This static storyboard does not establish duration, acceleration curves, camera response, collision accuracy, successful-hit timing, interruption behavior, or achieved rendering quality. Those need motion evidence from the real playable entry point and independent review after implementation.

## Exact generation prompt

```text
Use case: stylized-concept
Asset type: one saved 4-panel labeled motion storyboard reference for The Dead Wake opening stone-hatchet animation. Aspirational visual reference, NOT captured gameplay.
Input images: Image 1 is accepted art direction for natural gray worked stone, raw timber, cord, readable green daylight survival setting. Image 2 is actual current prototype reference for the SMALLER compact faceted hatchet silhouette, lower-right viewmodel placement and simple practical first-person HUD. Match the smaller scale from Image 2; do not reproduce its grass-obscured composition. No hand/arm is required or shown.
Primary request: ONE coherent landscape storyboard, target 2048x1152, arranged as a clean 2-by-2 grid of FOUR landscape game-view panels with consistent camera and same fallen timber target in all four. Small tasteful dark gutters and concise ivory labels. Top heading exactly "THE DEAD WAKE — HATCHET MOTION REFERENCE". Subheading exactly "VISUAL CONCEPT • TOOL POSES ONLY".
Composition in every panel: first-person eye view slightly downward toward an ordinary bark-covered fallen log in a sunlit grassy clearing with patches of exposed earth. Natural materials from Image 1, modest practical game-rendered look. Log lies diagonally across center and lower middle at reachable distance. Camera remains stationary; crosshair marks the SAME clear point on the log in every panel. Tool is a compact stone hatchet: shallow gray chipped wedge head, short rough timber handle, three tight brown cord wraps around head/haft junction, consistent exact design across all panels. Shaft enters from bottom right; NO hands, arms, character body, second tool, giant stone head, glowing effects, camera shake, elaborate new gameplay/UI. Mostly clear center with small tool movement focused on lower/right view; tool head usually about 18–22 percent of panel height.
Panel 1 top left, label exactly "1  READY": compact hatchet at lower right, head upright at approximately x80% y65%; center aim and target readable. Small caption exactly "Aim stays clear". No gain popup.
Panel 2 top right, label exactly "2  WIND-UP": same tool lifted modestly upward and outward to the right, handle angled back slightly; a short thin curved guide arrow shows limited backward/upward preparation. Keep head within frame and preserve apparent size. Caption exactly "Short lift • no resource award". No gain popup.
Panel 3 bottom left, label exactly "3  CONTACT": same hatchet swings forward/down diagonally left in a short arc; cutting edge visibly TOUCHES the marked point on the log. Blade correctly meets wood, shaft remains entering bottom right. A few tiny wood flecks at the precise contact, no fire/explosion. Small clear "+2 WOOD" text appears beside impact ONLY in this panel. Caption exactly "Impact feedback + award here". A thin curved guide arrow shows the short incoming path; do not obscure the contact with the arrow.
Panel 4 bottom right, label exactly "4  RECOVERY": tool lifted a little off wood and moving smoothly back toward its original lower-right ready position, small short curved return arrow. No new resource award and no second burst. Caption exactly "Return smoothly • no duplicate award".
Bottom note exactly "POSE AND TIMING INTENT — NOT GAMEPLAY CAPTURE".
Constraints: four visually DISTINCT yet coherent poses, identical tool scale/material/design and fixed camera/environment, practical short right-hand-side chop with clear ready → wind-up → contact → recovery. Only tool poses shown. Central aim remains readable except momentary blade contact. Restrained natural daylight, legible labels, no dense HUD, no invented gameplay features. Do not treat storyboard as evidence of existing animation.
```


## September 24 standing-tree implementation

The user replaced ground-log harvesting with wood from standing trees. The saved
storyboard remains a tool-pose reference only; its log target and illustrative
+2 label are superseded. Production uses the existing standing oak and +4 wood per
successful contact. No fallen-log gathering path remains.

The first actual 27.86 FPS camera recording exposed an oversized ready pose and
contact well below the aim point. The next revision uses 0.65 view scale, ready
position (34, -14, -17), preparation (32, -18, -13), contact (38, -5, -11) in eye
space. The contact event stays at 0.2 seconds. Actual evidence is under
`Docs/ValidationEvidence/DeadWake/Inventory/axe-contact-v2.webm`, with capture times
and extracted frames. Final independent visual acceptance is still pending.
