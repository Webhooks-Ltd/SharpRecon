const { execFileSync, spawn } = require("child_process");
const {
  existsSync,
  mkdirSync,
  readFileSync,
  writeFileSync,
  readdirSync,
  cpSync,
  rmSync,
  unlinkSync,
} = require("fs");
const { join } = require("path");
const os = require("os");
const https = require("https");
const { createWriteStream } = require("fs");
const { pipeline } = require("stream/promises");

const SCRIPT_DIR = __dirname;
const PUBLISH_DIR = process.argv[2] || join(SCRIPT_DIR, "src", "SharpRecon", "bin", "publish");
const REPO = "Webhooks-Ltd/SharpRecon";
const PLUGIN_JSON = join(SCRIPT_DIR, ".claude-plugin", "plugin.json");

function getRid() {
  const platform = os.platform();
  const arch = os.arch();
  const osMap = { linux: "linux", darwin: "osx", win32: "win" };
  const archMap = { x64: "x64", arm64: "arm64" };
  const o = osMap[platform];
  const a = archMap[arch];
  if (!o || !a) {
    process.stderr.write(`Unsupported platform: ${platform}-${arch}\n`);
    process.exit(1);
  }
  return `${o}-${a}`;
}

function httpsGet(url) {
  return new Promise((resolve, reject) => {
    https.get(url, { headers: { "User-Agent": "SharpRecon-Launcher" } }, (res) => {
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        return httpsGet(res.headers.location).then(resolve, reject);
      }
      if (res.statusCode !== 200) {
        res.resume();
        return reject(new Error(`HTTP ${res.statusCode} for ${url}`));
      }
      resolve(res);
    }).on("error", reject);
  });
}

function getPluginTag() {
  try {
    const data = JSON.parse(readFileSync(PLUGIN_JSON, "utf8"));
    return data.version ? `v${data.version}` : "";
  } catch {
    return "";
  }
}

async function installRelease(targetDir, tag) {
  const rid = getRid();
  const url = `https://github.com/${REPO}/releases/download/${tag}/sharp-recon-${rid}.tar.gz`;

  process.stderr.write(`Downloading SharpRecon (${rid}) from GitHub releases...\n`);

  const tmpFile = join(os.tmpdir(), `sharp-recon-${Date.now()}.tar.gz`);
  try {
    const res = await httpsGet(url);
    await pipeline(res, createWriteStream(tmpFile));
  } catch (err) {
    try { unlinkSync(tmpFile); } catch {}
    process.stderr.write(`Failed to download ${url} — have you created a release?\n`);
    process.exit(1);
  }

  rmSync(targetDir, { recursive: true, force: true });
  mkdirSync(targetDir, { recursive: true });

  execFileSync("tar", ["-xzf", tmpFile, "-C", targetDir], { stdio: "inherit" });
  unlinkSync(tmpFile);

  if (tag) writeFileSync(join(targetDir, ".release-tag"), tag);
  process.stderr.write(`Installed SharpRecon ${tag} to ${targetDir}\n`);
}

async function main() {
  const hasFiles = existsSync(PUBLISH_DIR) &&
    readdirSync(PUBLISH_DIR).some((f) => f.startsWith("SharpRecon"));

  const releaseTagFile = join(PUBLISH_DIR, ".release-tag");
  const localTag = existsSync(releaseTagFile) ? readFileSync(releaseTagFile, "utf8").trim() : "";

  const pluginTag = getPluginTag();

  if (!hasFiles) {
    await installRelease(PUBLISH_DIR, pluginTag);
  } else if (localTag && pluginTag && pluginTag !== localTag) {
    process.stderr.write(`Update available: ${localTag} -> ${pluginTag}\n`);
    await installRelease(PUBLISH_DIR, pluginTag);
  }

  const baseTemp = join(os.tmpdir(), "SharpRecon");
  mkdirSync(baseTemp, { recursive: true });

  try {
    for (const d of readdirSync(baseTemp)) {
      const p = join(baseTemp, d);
      const stat = require("fs").statSync(p);
      if (stat.isDirectory() && Date.now() - stat.mtimeMs > 60 * 60 * 1000) {
        rmSync(p, { recursive: true, force: true });
      }
    }
  } catch {}

  const shadowDir = join(baseTemp, `${Date.now()}-${Math.random().toString(36).slice(2)}`);
  mkdirSync(shadowDir, { recursive: true });

  process.on("exit", () => {
    try { rmSync(shadowDir, { recursive: true, force: true }); } catch {}
  });
  process.on("SIGINT", () => process.exit());
  process.on("SIGTERM", () => process.exit());

  cpSync(PUBLISH_DIR, shadowDir, { recursive: true });

  const rid = getRid();
  const exe = join(shadowDir, rid.startsWith("win") ? "SharpRecon.exe" : "SharpRecon");

  const child = spawn(exe, [], {
    stdio: "inherit",
    windowsHide: true,
  });

  child.on("exit", (code) => process.exit(code ?? 1));
}

main().catch((err) => {
  process.stderr.write(`Launcher error: ${err.message}\n`);
  process.exit(1);
});
