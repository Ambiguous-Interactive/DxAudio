# DSP node graph

`DspGraph` is engine-free; `DspGraphAsset` authors one as data. Named nodes each hold a `DspChain`, an edge carries one
node's block into a later node, and a node with several incoming edges sums them. Fan-out is a node with several
outgoing edges; fan-in is a node that takes several. One node is the input, which receives the caller's block, and one
is the output.

## Authoring

The asset stores nodes and edges by name, not by position, because a node added above another would otherwise move every
edge that crosses it. An edge to a name no node holds is refused, as is a second node with a name already in use, and
`FindProblems` names the node or edge a person has to fix. It also reports a node off the input-to-output path and a
graph it cannot prepare at `ReferenceSampleRate` for stereo `ReferenceBlockFrames`-frame blocks, which is where the
2 MiB budget is checked; another device can reserve a different figure. Two nodes may name one chain asset: each builds
its own chain. A node with no chain is a junction that passes its input on, which is how a split or a join with no
shaping of its own is authored. `TryCreateGraph` answers `false` and leaves no graph, so the audio path passes the block
through instead of running half a graph.

A cue (`SfxCue.Graph`) or a track (`MusicTrack.Graph`) runs its graph after the voice's chain and before the bus: a
branch sees the shaped signal, and the bus still acts on the sum. A voice builds its own graph for each block shape, so
two voices never share a branch buffer, and it keeps what it built the first time it met the asset. The cue inspector
and the library check report a graph the runtime cannot build, because it would pass the sound through unshaped; a host
also counts a block that only such a graph would have shaped in `SkippedBlocks`.

`TryGetNodeLatency` reports one branch's arrival, which is what the graph inspector shows beside each node, and
`LatencySamples` is the longest path, which the graph inspector shows as its own total.

## Alignment

Branches report different delay, so the graph aligns instead of only reporting the mismatch. A node arrives as late as
its latest predecessor plus its own chain delay, `A[n] = L[n] + max(A[u])`, and an edge `u -> v` waits
`A[v] - L[v] - A[u]` frames. Every path then reaches the output with the same age, `LatencySamples` is the longest path,
and a branch that needs no wait gets no buffer. Reporting the mismatch and leaving alignment to a caller was rejected:
nothing else in the package can insert that delay, and a fan-in of mismatched branches is a comb.

A graph keeps one input. A voice is one source, so a second input inside it has no meaning; two sources that share
effects are a mixer, whose gains and clocks need their own design, and this package has none.

## Rules

- A cycle is refused at the edge that would close it, and `TryPrepare` refuses a node off the path from input to output.
- `TryPrepare` budgets the node buffers before it prepares a chain, then the edge lines, and refuses a graph over 2 MiB
  by answering `false`; nothing on the prepare path throws. A node owns its chain, because a shared chain would run one
  delay line twice in a block.
- Bounds are 16 nodes and 32 edges; `PreparedBytes` reports what the graph reserved, so a caller can budget a pool.
- `Process` allocates nothing once prepared, and a refusal leaves the block untouched rather than half-processed.

## Measured

| Probe                                                      | Result                               |
| ---------------------------------------------------------- | ------------------------------------ |
| Two-branch graph, 10,000 stereo 256-frame blocks           | 0 bytes, 154 ms, about 350x realtime |
| Shifter and limiter branches, 2,048-frame mono, 48 kHz     | 44,804 bytes, latency 1,201          |
| Same block, shifter 1,201 and limiter 240 of latency       | one impulse at 1,201, no comb        |
| Four-node fan-in, 1,024-frame stereo, two limiter branches | 42,880 bytes                         |
| Same graph, 256-frame stereo / 1,024-frame mono            | 12,160 bytes / 21,440 bytes          |

A 0.25 impulse reaches the output once, at the shifter's delay, with twice the amplitude; without the edge delay the
limiter branch would arrive 961 frames early and the two would not sum. The prepared figure counts four node buffers of
2,048 mono floats and the 961-frame line the limiter branch waits on, and the same graph in 256-frame stereo blocks
reserves 17,928 bytes.

A warmed stereo voice holds the mono and stereo shapes of one graph asset: 64,320 bytes for the four-node fan-in above,
against the graph's own 2 MiB budget. A pool that warms many graph cues pays that for each voice separately.
