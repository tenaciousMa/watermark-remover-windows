const parts = window.location.pathname.split("/").filter(Boolean);
const owner = parts.length >= 2 ? parts[0] : "OWNER";
const repo = parts.length >= 2 ? parts[1] : "REPOSITORY";
const base = `https://github.com/${owner}/${repo}`;
const releases = `${base}/releases/latest`;

for (const id of ["repoLink", "releaseDownload", "releasePage", "primaryDownload"]) {
  const element = document.getElementById(id);
  if (!element) continue;
  if (id === "repoLink") element.href = base;
  else if (id === "releasePage") element.href = releases;
  else element.href = releases;
}
