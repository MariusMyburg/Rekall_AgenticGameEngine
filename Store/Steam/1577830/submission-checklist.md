# Rekall AGE — Steamworks Submission Checklist

App ID: `1577830`

Target status: Early Access

South African launch price: **exactly ZAR 500 / R500**

Worldwide prices: **unresolved until reviewed in Steam's pricing/conversion UI**

This checklist is a release gate. Do not submit the store page or set the build live while any blocking item remains unchecked.

## 1. App replacement and account checks

- [ ] Confirm the signed-in Steamworks account has authority to administer app `1577830`.
- [ ] Record the current app name, app type, packages, depots, builds, release state, wishlists, reviews, community content, achievements, cloud data, and store assets before changing anything.
- [ ] Confirm with Steamworks support that replacing the existing app with materially different game-development software is permitted for this app ID.
- [ ] Preserve an export or screenshots of the original configuration for rollback and audit.
- [ ] Confirm whether the app type can and should be changed to Software rather than Game.
- [ ] Remove old-product claims, genres, tags, screenshots, trailers, icons, achievements, controller declarations, system requirements, legal links, and descriptions only after the replacement is approved.
- [ ] Do not delete depots, packages, builds, cloud data, achievements, or community content without an explicit, recorded migration decision.

## 2. Product identity and legal owner

- [ ] Product name is `Rekall AGE`.
- [ ] Developer is `Rekall`, or replace it with the owner's exact approved public name.
- [ ] Publisher is `Rekall`, or replace it with the owner's exact approved public name.
- [ ] Confirm the exact contracting legal entity, jurisdiction, business address, and tax/banking identity.
- [ ] Supply a monitored support email address.
- [ ] Supply an official product/support website.
- [ ] Supply an approved privacy-policy URL describing local diagnostics and optional third-party provider connections.
- [ ] Review the proprietary notice against the license customers receive through Steam.
- [ ] Complete legal review of game/runtime redistribution, generated project ownership, bundled SDK/runtime use, example reuse, provider responsibility, warranties, and Early Access changes.
- [ ] Review and approve `END-USER-LICENSE-AGREEMENT.md` as the purchaser EULA before publishing it through Steam.

## 3. Third-party and content-rights gate

- [x] Generate a complete direct-and-transitive dependency inventory for the exact Steam payload (42 runtime NuGet packages were enumerated from every shipped `.deps.json`).
- [ ] Include every required third-party license and notice, not merely package URLs.
- [x] Include the Rain Glass CC BY 2.0 attribution in the installed notices.
- [x] Include the Stellar Dominion CC0 audio provenance notice.
- [ ] Verify commercial redistribution rights for every image, texture, model, sound, font, icon, logo, and screenshot in the payload and store page.
- [x] Exclude `Examples/GlbStationTest` from the Steam build and all store media unless the space-station GLB's rights are documented.
- [x] Exclude all local or ignored third-party engine checkouts, including `Examples/Stride/**` and `Examples/Prowl/**`.
- [x] Exclude the local “Faith of the Heart” file and every imported audio/texture asset not present in Studio's explicit redistribution allowlist.
- [ ] Confirm each allowlisted ignored import is checked into the release revision with its provenance record; never copy the developer's entire `Examples` directory.
- [x] Audit the final staged depot file-by-file for unexpected binaries, archives, source media, credentials, keys, logs, absolute developer paths, and personal information.

## 4. Approved bundled-example inventory

The Steam build and store copy target **18** bundled AGE projects. `GlbStationTest` is deliberately excluded pending provenance.

- [x] AetherfallCitadel
- [x] BouncingBall
- [x] ClockworkCanopy
- [x] CraterField
- [x] CustomMaterialShader
- [x] Galaga3D
- [x] MidnightRider
- [x] Pong3D
- [x] ProceduralModelingProbe
- [x] ProgrammableCompositorProbe
- [x] ProgrammableGeometryProbe
- [x] RainGlass
- [x] Ridgebreaker
- [x] Showcase3D
- [x] StellarDominion
- [x] SummitRun
- [x] TumblingCubes
- [x] VulkanCubeProbe

For each checked example:

- [ ] Its project opens from Studio's Examples menu as a writable copy.
- [ ] Required assets resolve without a developer-machine path.
- [ ] Required modules build or recover their receipts automatically.
- [ ] Its declared primary scene renders without an unhandled exception.
- [ ] Its licensing/provenance record is present.
- [ ] It does not contain ignored build, capture, cache, state, secret, or transaction-snapshot material.

## 5. Production build and depot staging

- [x] Use the installed Steamworks SDK at `C:\steamworks_sdk_164\sdk`; its existing scripts were inspected and the old MochiFriend scripts were archived before installing Rekall AGE upload scripts.
- [ ] Build the canonical Release `win-x64` distribution from a clean, approved revision.
- [x] Ensure the Steam payload is staged from an explicit approved inventory, not a broad working-tree glob.
- [x] Confirm Studio, CLI, MCP host, Windows Player, headless Player, portable module SDK, documentation, proprietary notice, and third-party notices are present.
- [x] Confirm the published Studio opens its bundled single-file HTML documentation through Help → Documentation and F1.
- [x] Confirm no source-repository dependency is required at runtime.
- [x] Confirm the package is self-contained for the .NET runtime.
- [ ] Confirm and disclose that the .NET 10 SDK is still required for C# module builds.
- [x] Run the installed-distribution acceptance gate from the staged build.
- [ ] Run focused launch and authoring acceptance on a clean Windows account.
- [ ] Run Vulkan acceptance on more than the development GPU if possible.
- [x] Record exact installed size and update system requirements before submission. The accepted payload is 638,523,491 bytes across 1,559 files; the 10 GB store requirement retains ample workspace and project headroom.
- [ ] Malware-scan the final depot payload.
- [ ] Sign binaries if an approved signing identity is available; otherwise disclose the unsigned Early Access state internally before release.
- [ ] Archive the exact commit, build logs, manifest, file hashes, Steam depot manifest IDs, and notices used for submission.

## 6. Launch configuration

- [ ] Set the primary Windows launch executable to the published Rekall AGE Studio executable.
- [ ] Set the working directory to the depot root or the executable's directory as required by the staged layout.
- [ ] Confirm launch succeeds through the Steam client, not only Explorer or `dotnet run`.
- [ ] Confirm Steam overlay behavior does not break the Vulkan child viewport or Player.
- [ ] Confirm spaces and non-ASCII characters in Steam library paths do not break Studio, examples, module builds, packaging, or Player launch.
- [ ] Confirm uninstall removes installed product files without removing user projects or local provider data.
- [ ] Confirm repair/reinstall restores documentation and bundled examples.
- [ ] Decide whether redistributables or prerequisite installation steps are required for the .NET 10 SDK, Vulkan drivers, or optional Ollama workflow.

## 7. Store-page basic information

- [ ] Name: `Rekall AGE`.
- [ ] Product type/category: Software / Game Development.
- [ ] Early Access is enabled.
- [ ] Developer and publisher strings are approved.
- [ ] Short description matches `store-copy.md`.
- [ ] Long description matches `store-copy.md` and renders correctly in Steam preview.
- [ ] Early Access Q&A matches `store-copy.md`.
- [ ] Early Access estimate is `18–24 months`.
- [ ] Current version is described as a Windows-first Developer Preview, not production-ready software.
- [ ] Store copy says 18 bundled projects, not 19.
- [ ] Experimental browser, multiplayer, OpenXR, virtual geometry, external asset generation, and 2D limitations are disclosed.
- [ ] No prior-product text or artwork remains.

## 8. Categories, tags, languages, and feature flags

- [ ] Select Software and the closest available Game Development category.
- [ ] Review proposed tags in `store-copy.md` and use only tags Steam actually permits.
- [ ] English is selected for interface support.
- [ ] Do not claim localized full audio or subtitles.
- [ ] Do not claim achievements, cards, Workshop, Cloud, matchmaking, Remote Play, or full controller support unless separately implemented and tested for the Steam product.
- [ ] Do not select VR-only or production-ready VR support; OpenXR remains experimental.
- [ ] Do not describe engine-level controller, multiplayer, or XR authoring contracts as Steam client features of Studio.

## 9. System requirements

- [ ] Validate Windows 10 64-bit as the minimum supported OS on a clean machine.
- [ ] Validate the minimum CPU, 16 GB RAM, Vulkan GPU, and 10 GB storage proposal.
- [ ] Validate the recommended Windows 11, eight-core CPU, 32 GB RAM, 8 GB VRAM, and 25 GB storage proposal.
- [ ] Replace candidate values if clean-machine evidence shows different requirements.
- [ ] State that a Vulkan-capable GPU and current driver are required.
- [ ] State that the .NET 10 SDK is required for C# gameplay-module authoring/building.
- [ ] State that Ollama and a separately downloaded model are required for local AI authoring.
- [ ] State that local model requirements vary and may exceed the base recommendations.
- [ ] State that cloud providers require the user's own credentials, internet connection, and may charge separately.

## 10. Pricing and packages

- [ ] In the Steam pricing UI, set South Africa to exactly **ZAR 500 / R500**.
- [ ] Do not infer worldwide amounts from ZAR 500 outside Steamworks.
- [ ] Review Steam's suggested conversions and every worldwide regional price before publishing.
- [ ] Resolve the package's base currency and worldwide prices in the Steam conversion UI.
- [ ] Record the approved worldwide price table and approver.
- [ ] State in Early Access Q&A that the price may increase at version 1.0.
- [ ] Do not advertise a specific 1.0 price until approved.
- [ ] Confirm tax, VAT, discount, cooldown, and release-discount implications.
- [ ] Confirm the correct store package grants app `1577830` and its intended depots.
- [ ] Confirm no obsolete previous-product packages or DLC are unintentionally sold.

## 11. Store artwork and screenshots

- [x] Create every capsule, header, library, logo, icon, and community asset in Steam's currently required dimensions and formats.
- [x] Use the exact `Rekall AGE` name and one coherent visual identity across all artwork.
- [x] Keep important text and marks inside Steam's safe areas.
- [x] Avoid tiny UI text in capsules.
- [x] Ensure screenshots are genuine current product captures, not mockups or unrelated concept art.
- [x] Include at least one clear Studio World-view screenshot showing hierarchy, Vulkan viewport, and Inspector.
- [ ] Include one Author-workspace screenshot showing provider selection and an operation log without credentials or personal paths.
- [x] Include one C# module/Code-workspace screenshot.
- [x] Include representative 2D and 3D project screenshots from rights-cleared bundled examples.
- [ ] Include a packaging/verification screenshot only if readable and visually useful.
- [x] Avoid GlbStationTest media pending provenance.
- [ ] Avoid RainGlass media unless the CC BY attribution obligation is satisfied in the store context.
- [x] Avoid any frame containing unlicensed soundtrack art, third-party logos, personal files, API keys, usernames, machine paths, or unrelated application chrome.
- [x] Confirm screenshot claims still match the submitted build after the final mutation.
- [x] Add captions that identify Studio, authored examples, and experimental features accurately.
- [ ] Create and review a trailer only if it shows the real author–inspect–simulate–play loop; do not imply guaranteed one-prompt completion.

## 12. Content survey and disclosures

- [ ] Complete Steam's content survey using the actual software, examples, screenshots, trailer, and linked sites.
- [ ] Review violence, weapons, flashing effects, user-generated content, online interaction, AI-generated content, and third-party service questions against shipped examples.
- [ ] Disclose generative-AI functionality accurately, including local and cloud providers and how generated output enters projects.
- [ ] Do not state that Rekall guarantees the legality, originality, safety, or commercial rights of model-generated output.
- [ ] Confirm whether user-authored games or imported assets count as user-generated content for Steam's current questionnaire.
- [ ] Confirm whether experimental network functionality changes any online-interaction disclosure.

## 13. Privacy, support, and operations

- [ ] Publish a privacy policy before store review.
- [ ] Explain that bounded diagnostics are stored locally and AGE does not automatically upload telemetry.
- [ ] Explain what project/prompt data is sent when the user deliberately selects a cloud provider.
- [ ] Explain where provider credentials are sourced and that they are not included in diagnostics.
- [ ] Establish a monitored Steam Discussions area and support mailbox.
- [ ] Publish a bug-report template requesting version, GPU/driver, mode, provider/model, project/scene, timestamp, first error, diagnostics, and reproduction steps.
- [ ] Define response expectations for purchase, launch, data-loss, security, and provider-billing issues.
- [ ] Prepare an Early Access known-issues post reflecting current experimental boundaries.
- [ ] Prepare a troubleshooting post for Vulkan availability, module receipts/builds, Ollama, provider authentication, and local diagnostic paths.

## 14. SteamPipe upload and branch acceptance

- [ ] Inspect the SDK's current example app/depot build scripts before creating app `1577830` scripts.
- [ ] Keep Steam credentials and sentry/auth files outside the repository and depot.
- [ ] Configure only the intended depots and content roots.
- [x] Upload the accepted payload first as a private, unassigned SteamPipe build; do not make it live or customer-visible.
- [x] Record the resulting build ID. SteamPipe build `25016850` was uploaded successfully for app `1577830`, depot `1577831`; SteamCMD did not print the depot manifest ID, so retrieve that value from Steamworks before branch assignment.
- [ ] Install the build through Steam onto a clean library directory.
- [ ] Launch Studio from Steam and exercise create, open, Examples, World, Inspector, Author, Code, Simulate, Play, documentation, package, and exit.
- [ ] Run at least one deterministic gameplay assertion and one real Windows Player session from the installed Steam build.
- [ ] Confirm the 18 approved examples appear and GlbStationTest does not.
- [ ] Confirm no ignored third-party repository or copyrighted local audio appears in the installed files.
- [ ] Verify install, update, repair, move-library, and uninstall behavior.
- [ ] Test offline launch and clearly distinguish offline core authoring from provider-dependent AI actions.
- [ ] Do not promote the build to the default branch until private-branch acceptance passes.

## 15. Store review and release readiness

- [ ] Preview every store section in desktop and mobile layouts.
- [ ] Check spelling, links, BBCode, artwork crops, language declarations, and system requirements.
- [ ] Confirm the build and store page are both submitted for Valve review.
- [ ] Resolve every review finding without weakening legal or Early Access disclosures.
- [ ] Confirm release timing, required waiting periods, and any Coming Soon requirement.
- [ ] Confirm ZAR 500 and the approved worldwide price table one final time.
- [ ] Confirm the reviewed build ID is the build selected for release.
- [ ] Publish launch notes, known issues, support links, privacy policy, and Early Access roadmap.
- [ ] Take final snapshots of store configuration, pricing, packages, depots, build selection, and release controls.
- [ ] Obtain owner approval immediately before the irreversible release action.

## Final go/no-go statement

Release is **NO-GO** if any of the following is unresolved:

- Valve has not approved replacement of the previous app/product.
- Any shipped asset lacks documented commercial redistribution rights.
- Ignored local audio or third-party engine checkouts appear in the depot.
- The exact Steam payload has not passed installed acceptance.
- The privacy/support/legal identity information is incomplete.
- Worldwide pricing has not been reviewed in Steamworks.
- Store claims or screenshots do not match the reviewed build.
- The selected release build ID differs from the accepted build.
