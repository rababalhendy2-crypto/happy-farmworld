extends Node
## All audio is synthesized at runtime: original, no licensing issues, near-zero APK size.
## Separate Music and SFX buses with independent volume.

const RATE := 22050

var _sounds: Dictionary = {}
var _players: Array[AudioStreamPlayer] = []
var _music: AudioStreamPlayer

func _ready() -> void:
	for bus in ["Music", "SFX"]:
		if AudioServer.get_bus_index(bus) == -1:
			AudioServer.add_bus()
			AudioServer.set_bus_name(AudioServer.bus_count - 1, bus)
			AudioServer.set_bus_send(AudioServer.bus_count - 1, "Master")
	_build_sounds()
	for i in 6:
		var p := AudioStreamPlayer.new()
		p.bus = "SFX"
		add_child(p)
		_players.append(p)
	_music = AudioStreamPlayer.new()
	_music.bus = "Music"
	_music.stream = _build_music()
	add_child(_music)
	_music.play()

func set_volumes(music: float, sfx: float) -> void:
	_set_bus("Music", music)
	_set_bus("SFX", sfx)

func _set_bus(bus: String, v: float) -> void:
	var i := AudioServer.get_bus_index(bus)
	AudioServer.set_bus_mute(i, v <= 0.001)
	AudioServer.set_bus_volume_db(i, linear_to_db(maxf(v, 0.001)))

func play(name: String, pitch: float = 1.0) -> void:
	if not _sounds.has(name):
		return
	for p in _players:
		if not p.playing:
			p.stream = _sounds[name]
			p.pitch_scale = pitch
			p.play()
			return

# ------------------------------------------------------------ synthesis

static func _note(n: float) -> float:
	return 440.0 * pow(2.0, (n - 69.0) / 12.0)

func _wav(samples: PackedFloat32Array, loop := false) -> AudioStreamWAV:
	var bytes := PackedByteArray()
	bytes.resize(samples.size() * 2)
	for i in samples.size():
		bytes.encode_s16(i * 2, int(clampf(samples[i], -1.0, 1.0) * 32000.0))
	var w := AudioStreamWAV.new()
	w.format = AudioStreamWAV.FORMAT_16_BITS
	w.mix_rate = RATE
	w.data = bytes
	if loop:
		w.loop_mode = AudioStreamWAV.LOOP_FORWARD
		w.loop_end = samples.size()
	return w

## tones: Array of [freq_start, freq_end, start_sec, dur_sec, volume, wave("sine"/"square"/"noise"/"saw"), vibrato]
func _synth(tones: Array, total: float) -> PackedFloat32Array:
	var out := PackedFloat32Array()
	out.resize(int(total * RATE))
	for t in tones:
		var f0: float = t[0]; var f1: float = t[1]
		var start := int(float(t[2]) * RATE); var n := int(float(t[3]) * RATE)
		var vol: float = t[4]; var wave: String = t[5]; var vib: float = t[6] if t.size() > 6 else 0.0
		var phase := 0.0
		for i in n:
			var k := start + i
			if k >= out.size():
				break
			var x := float(i) / n
			var f := lerpf(f0, f1, x) * (1.0 + vib * sin(i * TAU * 6.0 / RATE))
			phase += f / RATE
			var s := 0.0
			match wave:
				"sine": s = sin(phase * TAU)
				"square": s = 0.6 if fmod(phase, 1.0) < 0.5 else -0.6
				"saw": s = fmod(phase, 1.0) * 2.0 - 1.0
				"tri": s = abs(fmod(phase, 1.0) * 4.0 - 2.0) - 1.0
				"noise": s = randf_range(-1.0, 1.0)
			var env := minf(1.0, x * 40.0) * pow(1.0 - x, 1.5)
			out[k] += s * env * vol
	return out

func _build_sounds() -> void:
	_sounds["click"] = _wav(_synth([[880, 1100, 0, 0.06, 0.35, "sine"]], 0.07))
	_sounds["plant"] = _wav(_synth([[300, 180, 0, 0.12, 0.5, "sine"], [180, 140, 0, 0.08, 0.15, "noise"]], 0.14))
	_sounds["water"] = _wav(_synth([[900, 400, 0, 0.35, 0.18, "noise"], [600, 900, 0.05, 0.2, 0.2, "sine"]], 0.4))
	_sounds["harvest"] = _wav(_synth([[_note(72), _note(72), 0, 0.12, 0.35, "tri"], [_note(76), _note(76), 0.08, 0.12, 0.35, "tri"], [_note(79), _note(79), 0.16, 0.2, 0.35, "tri"]], 0.4))
	_sounds["coin"] = _wav(_synth([[_note(88), _note(88), 0, 0.08, 0.3, "square"], [_note(93), _note(93), 0.07, 0.22, 0.3, "square"]], 0.3))
	_sounds["error"] = _wav(_synth([[220, 180, 0, 0.18, 0.3, "tri"]], 0.2))
	_sounds["levelup"] = _wav(_synth([[_note(67), _note(67), 0, 0.15, 0.3, "tri"], [_note(72), _note(72), 0.13, 0.15, 0.3, "tri"], [_note(76), _note(76), 0.26, 0.15, 0.3, "tri"], [_note(84), _note(84), 0.39, 0.4, 0.35, "tri"]], 0.8))
	# Friendly, cartoon-like animal voices
	_sounds["cow"] = _wav(_synth([[130, 105, 0, 0.9, 0.5, "saw", 0.02]], 0.95))
	_sounds["chicken"] = _wav(_synth([[900, 700, 0, 0.07, 0.3, "square"], [950, 750, 0.12, 0.07, 0.3, "square"], [1100, 800, 0.24, 0.12, 0.3, "square"]], 0.4))
	_sounds["sheep"] = _wav(_synth([[340, 300, 0, 0.6, 0.35, "saw", 0.06]], 0.65))
	_sounds["goat"] = _wav(_synth([[420, 360, 0, 0.45, 0.3, "saw", 0.09]], 0.5))
	_sounds["pig"] = _wav(_synth([[160, 220, 0, 0.12, 0.35, "square", 0.05], [170, 230, 0.16, 0.12, 0.35, "square", 0.05]], 0.32))
	_sounds["rabbit"] = _wav(_synth([[1400, 1800, 0, 0.05, 0.2, "sine"], [1500, 1900, 0.09, 0.05, 0.2, "sine"]], 0.16))

func _build_music() -> AudioStreamWAV:
	# A calm, original pentatonic loop: 8 bars, 92 bpm, melody + soft bass.
	var beat := 60.0 / 92.0
	var melody := [76, 79, 81, 79, 76, 74, 72, 74, 76, 76, 79, 81, 84, 81, 79, 76,
		74, 76, 79, 76, 74, 72, 69, 72, 74, 76, 74, 72, 72, -1, 72, -1]
	var bass := [48, 48, 53, 53, 55, 55, 48, 48]
	var tones: Array = []
	for i in melody.size():
		if melody[i] > 0:
			var f := _note(melody[i])
			tones.append([f, f, i * beat, beat * 0.95, 0.12, "tri"])
	for b in bass.size():
		var f := _note(bass[b])
		tones.append([f, f, b * beat * 4, beat * 3.9, 0.10, "sine"])
		tones.append([f * 1.5, f * 1.5, b * beat * 4 + beat * 2, beat * 1.9, 0.05, "sine"])
	return _wav(_synth(tones, melody.size() * beat), true)
