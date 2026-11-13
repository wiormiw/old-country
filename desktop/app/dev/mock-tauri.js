// A stand-in for the Tauri bridge with demo data, so the UI can be looked at in a plain browser
// (`trunk serve`, then load this before the app; see the README). Used for the screenshots, which
// should never show anyone's real library.
(() => {
  const now = Date.now();
  const ago = (minutes) => new Date(now - minutes * 60_000).toISOString();
  const songs = [
    ["t1", "Never Gonna Give You Up", "Rick Astley", "00:03:33", "dQw4w9WgXcQ", true],
    ["t2", "Bohemian Rhapsody", "Queen", "00:05:55", "fJ9rUzIMcZQ", true],
    ["t3", "Smells Like Teen Spirit", "Nirvana", "00:05:01", "hTWKbfoikeg", false],
    ["t4", "Shape of You", "Ed Sheeran", "00:04:23", "JGwWNGJdvx8", false],
    ["t5", "Dust Road (home recording)", "Old Country Band", "00:03:35", null, true],
    ["t6", "Gangnam Style", "PSY", "00:04:12", "9bZkp7q19f0", true],
    ["t7", "Despacito", "Luis Fonsi", "00:04:41", "kJQP7kiw5Fk", false],
  ];
  const track = ([id, title, artist, duration, video, offline]) => ({
    id, title, artist, duration, isAvailableOffline: offline, codec: offline ? (video ? "Opus" : "Flac") : undefined,
    youTubeVideoId: video ?? undefined, createdAt: ago(600),
  });
  const tracks = songs.map(track);
  const playlists = [
    { id: "p1", name: "Long drive", description: "For the road out of town", trackCount: 5, createdAt: ago(9000), updatedAt: ago(35), youTubeListId: "PLdemo", youTubeSyncedAt: ago(90) },
    { id: "p2", name: "Sunday morning", trackCount: 3, createdAt: ago(20000), updatedAt: ago(1500) },
    { id: "p3", name: "Old favourites", description: "The ones that never get old", trackCount: 7, createdAt: ago(40000) },
  ];
  const entries = tracks.slice(0, 5).map((t, i) => ({
    position: i, trackId: t.id, title: t.title, artist: t.artist, duration: t.duration,
    isAvailableOffline: t.isAvailableOffline, youTubeVideoId: t.youTubeVideoId, addedAt: ago(100 * i),
  }));
  const item = (t) => ({ audio: { kind: "track", id: t.id }, title: t.title, artist: t.artist, duration: 213, videoId: t.youTubeVideoId });
  let theme = localStorage.getItem("old-country.theme") ?? "system";
  const player = {
    queue: tracks.slice(0, 4).map(item), index: 1, order: [0, 1, 2, 3], shuffle: false, repeat: "off",
    playing: true, buffering: false, position: 74, duration: 355, volume: 0.7,
  };
  if (location.hash === "#queue") Object.assign(player, { shuffle: true, repeat: "all", order: [2, 0, 1, 3], index: 0 });
  entries[2] = { ...entries[2], downloadProblem: "PremiumOnly" };
  const answers = {
    get_settings: () => ({ serverUrl: "http://192.168.1.10:5000", cacheMaxMegabytes: 2048, volume: 0.7, shuffle: false, repeat: "off", theme }),
    set_theme: ({ theme: t }) => { theme = t; return null; },
    check_server: () => null,
    player_state: () => player,
    list_tracks: () => ({ items: tracks, page: 1, pageSize: 50, totalCount: 1284, totalPages: 26 }),
    get_track: ({ id }) => ({ track: tracks.find((t) => t.id === id) ?? tracks[0], playlists: [{ id: "p1", name: "Long drive" }], bitrateKbps: 160, sampleRateHz: 48000 }),
    list_playlists: () => playlists,
    get_playlist: ({ id }) => ({ ...playlists.find((p) => p.id === id), entries, totalDuration: "00:24:27" }),
    scan_status: () => ({ isRunning: false, isQueued: false, lastScan: { trigger: "Scheduled", filesSeen: 1290, added: 4, alreadyKnown: 1280, unreadable: 0, missing: 6, startedAt: ago(62), finishedAt: ago(61) } }),
    youtube_downloads: () => ({ jobs: [
      { jobId: "j1", videoId: "JGwWNGJdvx8", title: "Shape of You", state: "Running", queuedAt: ago(3) },
      { jobId: "j2", videoId: "hTWKbfoikeg", title: "Smells Like Teen Spirit", state: "Queued", queuedAt: ago(2) },
      { jobId: "j3", videoId: "fJ9rUzIMcZQ", title: "Bohemian Rhapsody", state: "Completed", trackId: "t2", queuedAt: ago(40), finishedAt: ago(38) },
      { jobId: "j4", videoId: "kJQP7kiw5Fk", title: "Despacito", state: "Failed", errorCode: "youtube.premium_only", error: "This video is only available to Music Premium members", attempts: 1, queuedAt: ago(90), finishedAt: ago(89) },
    ] }),
    logs_folder: () => "~/.local/share/dev.wiormiw.oldcountry/logs",
    live_status: () => "live",
    youtube_search: () => [
      ["dQw4w9WgXcQ", "Never Gonna Give You Up", "Rick Astley", "00:03:33", "t1", true],
      ["yPYZpwSpKmA", "Together Forever", "Rick Astley", "00:03:25", null, false],
      ["AC3Ejf7vPEY", "Whenever You Need Somebody", "Rick Astley", "00:03:54", null, false],
      ["kHue-HaXXzg", "Cry for Help", "Rick Astley", "00:04:29", null, false],
    ].map(([videoId, title, artist, duration, trackId, offline]) => ({
      videoId, title, artist, duration, trackId: trackId ?? undefined, isAvailableOffline: offline,
      thumbnailUrl: `https://i.ytimg.com/vi/${videoId}/mqdefault.jpg`,
    })),
  };
  window.__TAURI__ = {
    core: {
      invoke: async (command, args) => {
        const answer = answers[command];
        return answer ? answer(args ?? {}) : null;
      },
    },
    event: { listen: async () => () => {} },
  };
})();
