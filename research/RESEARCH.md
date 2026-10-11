# Historical research brief

This brief is retained as the original proposal request. Its external output directory and
research-only scope describe that completed task. The user subsequently authorized Phase 0
implementation and relocation of research into this repository. Follow `../AGENTS.md`,
`11-development-roadmap.md` and `progress/phase-0.md` for current work.

# Project: AzerothCore Visual World Editor & Integrated Development Environment

## 1. Objective

The current workspace contains an existing World of Warcraft: Wrath of the Lich King (3.3.5a) terrain viewer developed using Godot.

Your task is to thoroughly investigate the existing project, relevant WoW client data formats, AzerothCore's architecture, and its database systems, then produce a comprehensive technical proposal for transforming this terrain viewer into a **fully integrated, 3D world editor, database editor, and development environment for AzerothCore and related WoW emulation projects.**

The long-term vision is an all-in-one development environment comparable in usability and workflow to Unity, Unreal Engine, or the Godot editor itself, but designed specifically for developing, modifying, scripting, and debugging World of Warcraft private server content.

The central design principle is:

**Developers should be able to create and modify WoW content through intuitive, visual, context-aware workflows without needing to understand or manually manipulate the underlying SQL database structures for most routine operations.**

This is a research, analysis, and architectural planning task. **Do not begin implementing the proposed editor.** Instead, deliver a sufficiently detailed and evidence-based proposal that another developer or AI coding agent could use to begin implementation confidently.

## 2. Available Research Resources

You have access to the following resources:

- **Existing workspace:** Current Godot-based WoTLK terrain viewer. This is the proposed foundation for the editor.
- **MySQL MCP**: Read-only copy of a test AzerothCore database for reference.
- **WoTLK client:** `X:\AzerothCore\OGClient`
- **AzerothCore source and databases:** `X:\AzerothCore\azerothcore-wotlk`
- **AzerothCore world database documentation:** [https://www.azerothcore.org/wiki/database-world](https://www.azerothcore.org/wiki/database-world)
- Additional official documentation, source repositories, format specifications, and relevant open-source tooling discovered during research.

All research documents, architectural proposals, diagrams, and supporting files must be written to:

`X:\AzerothCore\research`

Treat this directory as the exclusive output location.

Do not modify the existing Godot project, AzerothCore repository, client files, or databases. All research must be non-destructive. Do not connect to or modify a live database unless explicitly authorized.

## 3. Research Phase I: Existing Project Assessment

Begin by thoroughly exploring the Godot project.

Determine:

- Its current architecture, dependencies, and Godot version.
- How it loads, parses, and renders WoW terrain and assets.
- Which file formats it currently supports.
- How coordinate systems, terrain chunks, textures, materials, and object placement are represented.
- Which components are reusable for a full editor.
- Which components would require refactoring or replacement.
- Its current performance characteristics, limitations, and technical debt.

Investigate whether the current architecture can support interactive world editing, rather than viewing alone.

Identify the modifications necessary to evolve it into an extensible editor with scene hierarchies, object inspectors, selection tools, gizmos, undo/redo, asset browsers, and docking panels.

**Do not assume Godot is automatically the best long-term solution.** Evaluate its suitability, including editor extensibility, rendering performance, asset pipelines, and integration with external systems. Compare retaining Godot against alternative architectures where relevant.

Produce an evidence-based assessment of the existing project's readiness.

## 4. Research Phase II: AzerothCore Architecture and Data Model

Investigate AzerothCore's source code, SQL schemas, content systems, and runtime behavior.

Pay particular attention to the relationship between the WoW client, the AzerothCore world server, and its databases.

Research at minimum:

- World, character, and authentication databases.
- Creature templates, creature spawns, and movement systems.
- GameObject templates and placements.
- Quests, objectives, rewards, and prerequisite relationships.
- NPC dialogue, gossip menus, and interaction systems.
- Vendors, trainers, loot tables, and item templates.
- SmartAI, conditions, event systems, and waypoint scripting.
- AzerothCore modules and available scripting integrations, including Lua/Eluna where applicable.
- C++ scripting and compilation workflows.
- Spells, achievements, factions, and other interdependent content systems.
- Client-side DBC data and its relationship to server-side database records.
- Database migration and update mechanisms.

Identify how these systems interact, where their authoritative data resides, and how they could be exposed through visual editing interfaces.

For each major subsystem, distinguish between operations that can be performed entirely through database changes, those requiring client-side modifications, and those requiring server-side code or additional infrastructure.

**The editor must not simply be a visual SQL client.**

Investigate how higher-level development abstractions could turn complex database relationships into coherent development objects, such as NPCs, encounters, quests, interactive objects, and scripted events.

## 5. Research Phase III: WoW Asset and World Editing Capabilities

Investigate the technical requirements for a comprehensive 3D world editor.

Consider:

### World visualization and navigation

- Seamless loading and navigation of WoW maps.
- Terrain streaming, chunk loading, caching, and level-of-detail strategies.
- Display and selection of creatures, GameObjects, and world models.
- Accurate coordinate mapping between client world data and server-side spawn records.
- Searchable scene hierarchies and spatial queries.

### Interactive world editing

- Selecting, translating, rotating, and placing creatures and objects.
- Drag-and-drop asset placement from searchable palettes.
- Editing spawn properties through contextual inspectors.
- Visualizing NPC paths, patrol routes, and waypoint networks.
- Visualizing trigger regions, event locations, and relevant relationships.
- Multi-object editing, snapping, duplication, and undo/redo.

### Terrain and asset editing

Investigate the feasibility of:

- Terrain height editing and sculpting.
- Terrain texture painting.
- Editing or placing doodads, WMOs, and M2 models.
- Editing water, map chunks, and related world metadata.
- Importing, exporting, and repacking modified client assets.
- Producing modifications compatible with the WoTLK 3.3.5a client.

Consider formats such as ADT, WDT, WMO, M2, DBC, and MPQ, as applicable.

Distinguish clearly between **rendering existing terrain**, **editing server-side world content**, and **modifying client-side map assets**. These are separate technical challenges and may require separate implementation phases.

Investigate existing tooling and libraries that could reduce development effort. Evaluate their technical maturity, compatibility, licenses, and potential for reuse.

## 6. Research Phase IV: Unified Content Editing Experience

Design an editor experience that meaningfully improves the workflows of WoW content developers.

Explore the following integrated components:

### Visual world editor

A Unity-like scene view supporting object placement, inspection, manipulation, and contextual editing.

### Database abstraction layer

A typed, schema-aware data access system that maps AzerothCore's relational database structures to meaningful editor entities.

Developers should be able to edit a creature, quest, or encounter without manually tracking foreign keys and relationships across numerous SQL tables.

### Integrated content inspectors

Contextual property panels that expose relevant fields with descriptive labels, validation, autocomplete, and references to related entities.

### Visual quest editor

A potential node-based or structured workflow for creating quests, managing objectives, configuring rewards, and expressing prerequisite relationships.

### Visual scripting and event editor

Investigate the feasibility of representing SmartAI events, conditions, actions, and related scripting behaviors through node-based interfaces or other accessible abstractions.

Evaluate where visual scripting can realistically replace manual scripting and where direct Lua/C++ integration remains necessary.

### Integrated scripting IDE

Investigate support for:

- Source editing, navigation, and syntax highlighting.
- Code completion and symbol discovery.
- Lua/Eluna integration.
- SmartAI authoring and validation.
- C++ source navigation and related development workflows.
- Build, deployment, and diagnostic integration.
- Debugging capabilities where technically feasible.

### Asset browser

A searchable and previewable asset catalog containing client models, textures, game objects, and related content.

### Validation and testing tools

Explore schema validation, referential integrity checks, conflict detection, content consistency rules, and testing workflows.

Evaluate whether changes can be previewed or tested against a running AzerothCore instance and what would be needed to support such functionality safely.

## 7. Research Phase V: Architecture and Technical Design

Propose an extensible software architecture capable of supporting the long-term vision.

Address:

- Editor frontend and user interface architecture.
- Godot integration and any necessary engine modifications.
- Asset import, parsing, caching, and rendering.
- Database connectivity and data persistence.
- Schema-aware entity models.
- Editing transactions, undo/redo, and change tracking.
- SQL generation, validation, and migration compatibility.
- Scripting backends and IDE services.
- World-server communication and possible live editing.
- Plugin architecture.
- Project/workspace management.
- Logging, diagnostics, and error recovery.
- Backup, rollback, and version control integration.

Determine how to separate **editor-facing representations** from **AzerothCore-specific persistence formats**.

Evaluate a design that could eventually support other WoW emulator projects and expansion versions through interchangeable adapters, while using AzerothCore WoTLK as the initial implementation target.

Pay special attention to preventing data corruption, supporting reversible operations, and ensuring that editor-generated changes remain compatible with AzerothCore's established update and migration workflows.

Recommend concrete technologies, libraries, frameworks, and architectural patterns, explaining their trade-offs rather than merely listing options.

## 8. Research Phase VI: Developer Experience and Workflows

Illustrate how the completed editor would be used for common development tasks.

Include detailed, step-by-step user workflows for scenarios such as:

1. Opening an existing AzerothCore world and placing a new NPC.
2. Creating a new creature template and configuring its behavior.
3. Designing a quest and connecting it to NPCs, items, and objectives.
4. Creating an NPC patrol route directly within the 3D world.
5. Constructing an interactive encounter using SmartAI or Lua.
6. Editing an existing GameObject and its interactions.
7. Modifying terrain or placing client-side world assets.
8. Testing a change and safely deploying it to an AzerothCore server.
9. Identifying and resolving a broken database relationship.
10. Extending the editor with support for an additional emulator core.

For each workflow, explain:

- What the developer sees and interacts with.
- What the editor manages automatically.
- Which client, server, database, or script changes are required behind the scenes.
- Any technical limitations.
- The difference between an ideal long-term implementation and a realistic first version.

Use these workflows to inform the architectural proposal and development priorities.

## 9. Feasibility, Risks, and Limitations

Critically evaluate the proposal.

Do not treat all desired functionality as equally feasible.

Identify:

- Features achievable using existing AzerothCore APIs and database structures.
- Features requiring substantial new infrastructure.
- Features constrained by the WoTLK client's architecture.
- Features requiring additional client asset editing tools.
- Features that may be impractical or disproportionately expensive.
- Licensing, redistribution, and intellectual property considerations.
- Risks from AzerothCore schema changes and emulator version differences.
- Performance, reliability, and maintainability concerns.

Distinguish verified findings from hypotheses and unresolved questions.

Where appropriate, propose proof-of-concept experiments to validate architectural assumptions before major implementation work begins.

## 10. Development Roadmap

Produce a prioritized, incremental implementation strategy.

Do not recommend attempting the entire editor at once.

Organize the roadmap into coherent phases, such as:

**Phase 0 — Foundation and architectural validation**

Assess and stabilize the existing Godot project, verify critical file format assumptions, and prototype database integration.

**Phase 1 — Visual world database editor**

Implement world navigation, server-side spawn visualization, entity selection, and basic editing of creatures and GameObjects.

**Phase 2 — Integrated content authoring**

Develop higher-level editing workflows for creature templates, quests, NPC interactions, waypoints, and related database-backed systems.

**Phase 3 — Visual scripting and IDE integration**

Introduce SmartAI editing, scripting support, validation, and advanced development tooling.

**Phase 4 — Client-side world modification**

Investigate and incrementally implement terrain sculpting, texture painting, asset placement, and compatible client patch generation.

**Phase 5 — Advanced development environment**

Add advanced testing, diagnostics, plugin support, additional emulator compatibility, and other features justified by earlier phases.

Revise this phase structure if your findings suggest a better sequence.

For each phase, specify:

- Goals and expected developer value.
- Prerequisites and technical dependencies.
- Architecture and implementation tasks.
- Estimated relative complexity and major risks.
- Concrete milestones.
- Objective acceptance criteria.
- Expected deliverables.
- Dependencies on previous phases.

Identify a **minimum viable product (MVP)** that can provide immediate utility to AzerothCore developers without requiring full terrain editing or a complete IDE.

## 11. Required Research Deliverables

Create a well-organized collection of Markdown documents under:

`X:\AzerothCore\research`

Use the following structure as a starting point:

```text
research/
├── README.md
├── 01-executive-summary.md
├── 02-existing-project-assessment.md
├── 03-azerothcore-architecture.md
├── 04-client-formats-and-assets.md
├── 05-feature-specifications.md
├── 06-editor-architecture.md
├── 07-database-abstraction.md
├── 08-scripting-and-ide.md
├── 09-user-workflows.md
├── 10-feasibility-and-risks.md
├── 11-development-roadmap.md
├── 12-mvp-specification.md
├── 13-open-questions.md
└── references/
    ├── source-references.md
    └── research-notes.md
```

You may add documents, subdirectories, diagrams, component specifications, data models, interface sketches, or research artifacts where useful.

The documentation must be detailed enough to serve as the foundation for an actual development project rather than a high-level concept document.

### Documentation standards

- Use clear, structured Markdown.
- Include architecture diagrams where helpful, using Mermaid.
- Refer to specific source files, classes, functions, schemas, and tables whenever possible.
- Support technical claims with citations to inspected source code or documentation.
- When referencing local code, include repository-relative paths and relevant symbols.
- When relying on external sources, provide URLs.
- Clearly distinguish confirmed behavior, proposed behavior, and unverified assumptions.
- Compare alternatives and explain architectural decisions.
- Document blockers and unknowns instead of inventing answers.
- Avoid redundant generic descriptions across documents.
- Where appropriate, provide illustrative interfaces, pseudocode, and data schemas, clearly marked as proposals rather than existing implementations.

## 12. Research Quality and Execution Requirements

Follow these principles throughout the task:

1. **Inspect before proposing.** Analyze the actual workspace, source code, and database structures before making architectural recommendations.
2. **Prioritize accuracy over assumptions.** Never invent functionality, repository structures, APIs, or compatibility guarantees.
3. **Design for genuine usability.** The purpose is to reduce the cognitive burden of emulator development, not merely reorganize existing SQL tools.
4. **Preserve compatibility.** Proposals should respect AzerothCore's existing data formats and development conventions wherever practical.
5. **Prefer incremental development.** Evaluate reuse of existing systems before proposing extensive rewrites.
6. **Design for extensibility.** Avoid deeply coupling the entire editor to one specific database schema where an adapter-based design would be practical.
7. **Challenge the vision constructively.** Identify simpler, more sustainable alternatives whenever they offer comparable developer value.
8. **Keep research non-destructive.** Make no changes outside the designated research directory.
9. **Do not substitute speculative feature lists for technical analysis.**
10. **Leave implementation-ready guidance.** Major recommendations should be actionable and sufficiently specific to translate into development tasks.

## 13. Final Outcome

At completion, the research directory should contain a comprehensive proposal answering five central questions:

1. **What do we already have?** An accurate assessment of the existing Godot terrain viewer and reusable capabilities.
2. **What can we realistically build?** An evidence-based assessment of editor features and their technical requirements.
3. **How should it be built?** A concrete, extensible architecture grounded in AzerothCore and WoTLK internals.
4. **What should we build first?** A tightly scoped MVP delivering meaningful improvements to existing content creation workflows.
5. **How do we get from the MVP to the full vision?** A structured development roadmap with realistic dependencies, priorities, and acceptance criteria.

The final product of this task is **a complete technical research and implementation proposal, not a functioning editor**.

Ensure every deliverable is saved in `X:\AzerothCore\research`.

When finished, provide a concise summary of the strongest architectural recommendation, the proposed MVP, the largest technical risks, and an index of all documents created.
