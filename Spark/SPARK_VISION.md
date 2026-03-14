# 🔥 Spark Vision — Dreams & Roadmap

> A living plan document for where Spark is headed.
> Everything below is aspirational — treat it as a design compass, not a contract.

---

## Current State (v0.x)

| Tab | Status | Notes |
|-----|--------|-------|
| 🎨 Concept Art | ✅ Working | SD Forge integration, prompt stacks, LoRA, creative engine |
| 🎵 Music Director | ✅ Working | MusicGen, config-driven instruments/composers/genres/notation |
| 🔊 Sound FX | ✅ Working | MusicGen for SFX, comprehensive preset library, category defaults |

---

## Phase 1 — Audio Polish (Near-Term)

### 🔊 SFX Post-Processing
- **Crop/trim tool** — The MusicGen engine has a minimum duration of 1 second, but many SFX
  (clicks, ticks, gunshots) are naturally < 1s. Add a waveform crop tool that lets the user
  trim generated audio to the precise window they need, then export the cropped clip.
- **Fade in/out** — Envelope editor for smooth transitions.
- **Pitch shift** — Quick ±12 semitone adjustment for variant creation.
- **Normalize / compress** — One-click loudness normalization.
- **Batch export** — Export selected clips as WAV/MP3/OGG with configurable sample rate.

### 🎵 Music Enhancements
- **Loop detection** — Analyze and mark loop points for seamless in-game looping.
- **Stem separation** — If possible, split generated music into stems (drums, bass, melody).
- **Layered generation** — Generate accompaniment over existing stems.
- **Transition builder** — Generate musical transitions between two themes.

---

## Phase 2 — Voice Tab (Mid-Term)

### 🗣 Voice Director
A dedicated tab for generating character dialogue, narration, and vocal performances.

**Planned features:**
- **Character voice profiles** — Define characters with voice descriptors (pitch, tone, accent, age, gender).
- **Emotion presets** — happy, sad, angry, scared, sarcastic, whispering, shouting, etc.
- **Text-to-speech integration** — Connect to TTS backends (Bark, XTTS, Coqui, ElevenLabs).
- **Voice cloning** — Record or import a reference voice clip for style transfer.
- **Batch dialogue** — Import a script file (CSV/JSON with character + line) and generate all lines.
- **Lip-sync data export** — Generate phoneme timing data for animation sync.
- **Multi-language support** — Generate same lines in multiple languages from one script.
- **Voice effects** — Radio filter, cave echo, underwater distortion, demonic, robotic.
- **A/B comparison** — Side-by-side playback of takes for the same line.

**Data model:**
```
VoiceProfile → { name, gender, age, accent, pitch, speed, reference_clip }
DialogueLine → { character, text, emotion, take_number, file_path }
```

---

## Phase 3 — Character Designer Tab

### 🧑‍🎨 Character Designer
A unified workspace for designing game/story characters that ties together visual,
audio, and narrative elements.

**Planned features:**
- **Character sheet** — Name, backstory, personality traits, faction, role.
- **Visual reference board** — Generate concept art variations for a character using the
  Concept Art engine with character-specific prompt templates.
- **Turnaround generator** — Auto-generate front/side/back/¾ views from a single description.
- **Expression sheet** — Generate facial expressions: neutral, happy, angry, sad, surprised, etc.
- **Outfit variants** — Design multiple costumes/armor sets for the same character.
- **Voice binding** — Link a Voice Profile to the character for dialogue generation.
- **Stat cards** — Optional RPG-style stat block (STR, DEX, INT, …) with visual layout.
- **Export pack** — Bundle all character assets (art, voice clips, data) as a zip.

**Data model:**
```
Character → { name, backstory, traits[], visual_prompts[], voice_profile, stat_block }
```

---

## Phase 4 — Setting Designer Tab

### 🏰 Setting / Environment Designer
Design game worlds, levels, and environments.

**Planned features:**
- **Location sheets** — Name, description, mood, time-of-day, weather, inhabitants.
- **Panorama generation** — Wide-format environment concept art.
- **Ambient soundscape** — Auto-compose layered ambient audio for the location
  (e.g., "cave + dripping water + distant monster growls + echo").
- **Lighting studies** — Generate the same scene at dawn, noon, dusk, night, storm.
- **Map sketch assist** — Basic top-down layout sketches for dungeon/level design.
- **Prop catalog** — Generate and catalog props/furniture/items for each location.
- **Transition paths** — Define how locations connect (corridors, portals, roads)
  and generate transitional art/audio.

**Data model:**
```
Location → { name, description, mood, ambient_layers[], concept_images[], props[], connections[] }
```

---

## Phase 5 — Animation & Motion (Long-Term)

### 🎬 Animation Workshop
Bring static concept art to life.

**Exploration areas:**
- **Sprite sheet generator** — From a character concept, generate walk/run/attack/idle
  sprite frames for 2D games.
- **Parallax layers** — Split an environment painting into depth layers for parallax scrolling.
- **Particle effect designer** — Design fire, smoke, magic, rain particle effects with
  preview animation.
- **Cutscene storyboard** — Chain generated images into a visual storyboard with
  timing, transitions, and dialogue placement.
- **AI video integration** — When video generation models mature (Runway, Pika, Sora),
  integrate for short animation clips from scene descriptions.

**Technical notes:**
- Sprite sheets could use img2img with consistent character + pose control.
- Parallax separation might use depth estimation models.
- Particle effects could be procedural (code-generated) rather than AI-generated.

---

## Phase 6 — Movie / Cinematic Generation (Dream)

### 🎥 Cinematic Director
The ultimate dream — generate short cinematic sequences.

**Vision:**
- **Scene script** — Write a scene with dialogue, action descriptions, camera directions.
- **Shot list generation** — AI breaks script into shots with camera angles.
- **Per-shot generation** — Each shot gets concept art + voice + SFX + music.
- **Timeline editor** — Arrange shots on a timeline with transitions.
- **Video synthesis** — Use emerging video-gen models to create actual clips.
- **Export** — Render as video file with all audio mixed.

**This is years out** but the architecture should be designed so each phase
builds toward it. Characters, settings, voice, music, SFX — all feed into
the cinematic pipeline.

---

## Architecture Notes

### Tab Architecture
Each major feature gets:
1. A **config JSON file** — `{feature}_config.json` with all creative options
2. A **ViewModel** — `{Feature}ViewModel.cs` with generation/playback/library logic
3. A **Panel XAML** — `Controls/{Feature}Panel.xaml` for the UI
4. A **Track/Record model** — Data class for the catalog/library

### Shared Infrastructure
- **MusicGenClient** — Shared by Music + SFX (and potentially Voice if using AudioCraft)
- **LogViewModel** — Shared log across all tabs
- **DocumentStore** — Story context for all creative engines
- **PreferenceTracker** — Could extend to track audio preferences too
- **WaveformVisualizer** — Reusable for any audio tab

### Future Service Backends
| Feature | Candidate Backends |
|---------|-------------------|
| Voice | Bark, XTTS v2, Coqui TTS, ElevenLabs API |
| Animation | Stable Video Diffusion, AnimateDiff, Deforum |
| Video | Runway Gen-2, Pika, Sora (when available) |
| Sprites | ControlNet + img2img, consistent-character workflows |

---

## Principles

1. **Config-driven** — All creative options in JSON. Users can customize without recompiling.
2. **Offline-first** — Prefer local models. Cloud APIs are optional accelerators.
3. **Everything is a library** — Every generated asset goes into a searchable, rateable catalog.
4. **Composable** — Characters reference voice profiles. Settings reference ambient layers.
   Cinematics reference everything. Build the graph.
5. **Non-destructive** — Soft-delete, version history, variant tracking. Never lose work.

---

*Last updated: auto-generated with initial creative expansion.*
*Mozart was here first. Then Beethoven. Then Bach. Then everybody else, alphabetically.* 🎹
