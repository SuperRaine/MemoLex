"""Sync the release packages to Quark and prepare the matching Sites update."""

import argparse
import hashlib
import json
import re
import subprocess
import tempfile
import time
from pathlib import Path


REPOSITORY = "SuperRaine/MemoLex"
QUARK_FOLDER = "墨墨搜词"
HOMEPAGE = "https://memolex.silverchina5.chatgpt.site/"
SHARE_URL = re.compile(r"https://pan\.quark\.cn/s/[A-Za-z0-9_-]+/?")
VERSION_TAG = re.compile(r"v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)")


def run(command, *, directory=None, timeout=600):
    return subprocess.run(command, cwd=directory, capture_output=True, encoding="utf-8-sig",
                          errors="replace", timeout=timeout)


def github(executable, arguments):
    result = run([executable, "release", *arguments, "--repo", REPOSITORY])
    if result.returncode:
        raise RuntimeError(f"GitHub {arguments[0]} 失败（退出码 {result.returncode}）。")
    return result.stdout


def release_snapshot(executable, tag, expected_names):
    release = json.loads(github(executable, ["view", tag, "--json", "tagName,targetCommitish,isDraft,isPrerelease,assets,body"]))
    if release["tagName"] != tag or release["isPrerelease"]:
        raise RuntimeError("请指定正式版本的发布草稿。")
    assets = release["assets"]
    if sorted(asset["name"] for asset in assets) != sorted(expected_names):
        raise RuntimeError("发布页应包含 Windows 安装版、Windows 便携版和 macOS 安装包。")
    for asset in assets:
        if asset["state"] != "uploaded" or asset["size"] <= 0 or not re.fullmatch(r"sha256:[0-9a-f]{64}", asset.get("digest") or ""):
            raise RuntimeError(f"附件「{asset['name']}」尚未上传完成或缺少校验值。")
    return release, {asset["name"]: (asset["size"], asset["digest"][7:]) for asset in assets}


def check_release_sequence(executable, tag):
    latest = json.loads(github(executable, ["view", "--json", "tagName,isDraft,isPrerelease"]))
    previous = VERSION_TAG.fullmatch(latest.get("tagName", ""))
    if previous is None or latest["isDraft"] or latest["isPrerelease"]:
        raise RuntimeError("无法确认上一版正式发布的版本号。")
    major, minor, patch = map(int, previous.groups())
    next_tag = "v2.0.0" if previous[0] == "v1.20.0" else f"v{major}.{minor}.{patch + 1}"
    if tag not in (previous[0], next_tag):
        raise RuntimeError(f"下一个正式版本应为 {next_tag}。当前版本可重新运行以补齐同步。")


def matches(path, asset):
    size, expected = asset
    if not path.is_file() or path.stat().st_size != size:
        return False
    digest = hashlib.sha256()
    with path.open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest() == expected


class QuarkDrive:
    def __init__(self, cli, node, scratch):
        self.cli = cli
        self.node = node
        self.scratch = scratch

    def call(self, *arguments):
        process = run([self.node, str(self.cli), *arguments], directory=self.cli.parent.parent)
        rows = []
        for line in process.stdout.splitlines():
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                continue
            if isinstance(row, dict):
                rows.append(row)
        result = next((row for row in reversed(rows) if row.get("type") == "result"), {})
        if process.returncode or result.get("code") != 0:
            code = result.get("code", process.returncode)
            if code in (-103, -1408):
                raise RuntimeError("请在发布电脑完成夸克授权后重试。")
            if code == 23018:
                raise RuntimeError("夸克的接口下载大小限制挡住了同名文件校验，请使用原发布电脑重新同步。")
            raise RuntimeError(f"夸克 {arguments[0]} 失败（{code}）。")
        return result.get("data") or {}, rows

    def folder(self, name, parent):
        data, _ = self.call("create-folder", "--dir-path", name, "--parent-fid", parent)
        if not isinstance(data.get("fid"), str) or not data["fid"]:
            raise RuntimeError(f"未能确认夸克文件夹「{name}」。")
        return data["fid"]

    def contents(self, folder):
        data, _ = self.call("browse", "--parent-fid", folder, "--all", "--task-dir", str(self.scratch))
        path = Path(data["file_path"]).resolve(strict=True)
        if data.get("hasMore") or not path.is_relative_to(self.scratch):
            raise RuntimeError("夸克未返回完整的版本目录。")
        with path.open(encoding="utf-8-sig") as file:
            return [json.loads(line) for line in file if line.strip()]

    def upload(self, path, folder, size):
        data, rows = self.call("upload", str(path), "--parent-fid", folder)
        files = [row.get("data", {}) for row in rows if row.get("type") == "list" and row.get("code") == 0]
        if len(files) != 1 or data.get("successCount") != 1:
            raise RuntimeError(f"夸克未确认收到「{path.name}」。")
        file = files[0]
        if file.get("fileName") != path.name or file.get("fileSize") != size or not file.get("fileId"):
            raise RuntimeError(f"夸克收到的文件与「{path.name}」不一致。")

    def uploaded_item(self, folder, name):
        for attempt in range(3):
            if attempt:
                time.sleep(1)
            item = matching_items(self.contents(folder), name)
            if item is not None:
                return item
        raise RuntimeError(f"夸克版本目录尚未显示「{name}」，请重试。")

    def check_existing(self, item, name, asset):
        # Existing copies may have been placed manually or left by an interrupted release.
        with tempfile.TemporaryDirectory(dir=self.scratch) as directory:
            self.call("download", "--fid", item["fid"], "--output-dir", directory)
            if not matches(Path(directory) / name, asset):
                raise RuntimeError(f"夸克现有文件「{name}」与 GitHub 附件的校验值不同。")

    def share(self, folder, version):
        data, _ = self.call("share", folder, "--title", f"{QUARK_FOLDER} {version}", "--url-type", "1", "--expired-type", "1")
        url = data.get("share_url", "")
        if not SHARE_URL.fullmatch(url):
            raise RuntimeError("夸克未返回可公开访问的下载链接。")
        return url

    def verify_share(self, url, version, assets):
        if not SHARE_URL.fullmatch(url):
            raise RuntimeError("夸克分享链接格式不正确。")
        data, _ = self.call("share-detail", "--url", url)
        items = data["files"]
        if len(items) == 1 and items[0].get("filename") == version and str(items[0].get("file_type")) == "0":
            data, _ = self.call("share-detail", "--url", url, "--pdir-fid", items[0]["fid"])
            items = data["files"]
        if len(items) != len(assets) or any(str(item.get("file_type")) != "1" for item in items):
            raise RuntimeError("夸克分享目录应只包含该版本的三个软件包。")
        if {item["filename"]: item["size"] for item in items} != {name: asset[0] for name, asset in assets.items()}:
            raise RuntimeError("夸克分享页上的文件与 GitHub 附件不一致。")


def matching_items(items, name):
    found = [item for item in items if item.get("filename") == name]
    if len(found) > 1:
        raise RuntimeError(f"夸克版本目录有多个同名项「{name}」。")
    return found[0] if found else None


def file_receipt(item, name, asset):
    if not item or str(item.get("file_type")) != "1" or item.get("size") != asset[0] or not item.get("fid"):
        raise RuntimeError(f"夸克版本目录中的「{name}」尚未通过核对。")
    # The listing's prefix changes on each request. The final component identifies the file.
    return {"fid": item["fid"].rsplit("|", 1)[-1], "updated_at": item.get("updated_at"), "sha256": asset[1]}


def load_receipts(path, folder):
    if path.is_file():
        saved = json.loads(path.read_text(encoding="utf-8"))
        if saved.get("folder") == folder:
            return saved["files"], saved.get("share_url", "")
    return {}, ""


def write_json(path, value):
    path.parent.mkdir(exist_ok=True)
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent, delete=False, newline="\n") as file:
        json.dump(value, file, ensure_ascii=False, indent=2)
        file.write("\n")
    Path(file.name).replace(path)


def prepare_site(site, node, version, share_url):
    write_json(site / "release.json", {"version": version, "quarkUrl": share_url})
    result = run([node, "build.mjs"], directory=site)
    if result.returncode:
        raise RuntimeError("软件主页生成失败，请检查 Sites 源码后重试。\n" + result.stderr)


def publish(args):
    tag = args.tag if args.tag.startswith("v") else "v" + args.tag
    if not VERSION_TAG.fullmatch(tag):
        raise RuntimeError("请输入形如 v2.0.0 的版本号。")
    site = args.site.resolve(strict=True)
    hosting = json.loads((site / ".openai" / "hosting.json").read_text(encoding="utf-8"))
    if not hosting.get("project_id") or hosting.get("static", {}).get("directory") != "dist" or not (site / "build.mjs").is_file():
        raise RuntimeError("请指定墨墨搜词的 Sites 源码目录。")
    check_release_sequence(args.gh, tag)
    version = tag[1:]
    prefix = f"MemoLex-{version}"
    names = [f"{prefix}-Windows-x64-Setup.msi", f"{prefix}-Windows-x64.zip", f"{prefix}-macOS-universal.dmg"]
    release, assets = release_snapshot(args.gh, tag, names)
    with tempfile.TemporaryDirectory(prefix="MemoLex-publish-") as directory:
        scratch = Path(directory).resolve()
        quark = QuarkDrive(Path(args.quark_cli).resolve(strict=True), args.node, scratch)
        folder = quark.folder(version, quark.folder(QUARK_FOLDER, "0"))
        receipt_path = quark.cli.parent.parent / "memolex-release-sync" / f"{tag}.json"
        receipts, share_url = load_receipts(receipt_path, folder)
        initial = quark.contents(folder)
        verified = {}
        for name, asset in assets.items():
            existing = matching_items(initial, name)
            if existing is not None:
                receipt = file_receipt(existing, name, asset)
                if receipt != receipts.get(name):
                    print(f"核对夸克上的 {name}", flush=True)
                    quark.check_existing(existing, name, asset)
            else:
                path = args.packages / name if args.packages else scratch / name
                if not matches(path, asset):
                    path = scratch / name
                    print(f"下载 GitHub 附件 {name}", flush=True)
                    github(args.gh, ["download", tag, "--pattern", name, "--dir", str(scratch)])
                if not matches(path, asset):
                    raise RuntimeError(f"附件「{name}」与 GitHub 提供的校验值不同。")
                print(f"同步到夸克 {name}", flush=True)
                quark.upload(path.resolve(), folder, asset[0])
                receipt = file_receipt(quark.uploaded_item(folder, name), name, asset)
            verified[name] = receipts[name] = receipt
            write_json(receipt_path, {"folder": folder, "files": receipts, "share_url": share_url})

        final = quark.contents(folder)
        if len(final) != len(assets):
            raise RuntimeError(f"请检查夸克的 {version} 目录，分享目录应只包含三个软件包。")
        for name, receipt in verified.items():
            if file_receipt(matching_items(final, name), name, assets[name]) != receipt:
                raise RuntimeError(f"夸克版本目录的「{name}」在同步期间发生了变更，请重试。")
        if not share_url:
            share_url = quark.share(folder, version)
            write_json(receipt_path, {"folder": folder, "files": receipts, "share_url": share_url})
        quark.verify_share(share_url, version, assets)
        print(f"已核对夸克「{QUARK_FOLDER}/{version}」的三个软件包和公开链接。", flush=True)

        prepare_site(site, args.node, version, share_url)
        current, current_assets = release_snapshot(args.gh, tag, names)
        if current_assets != assets or current["targetCommitish"] != release["targetCommitish"]:
            raise RuntimeError("同步期间 GitHub 附件或发布提交有变更，请重新运行。")
        if current["isDraft"]:
            check_release_sequence(args.gh, tag)
            notes = scratch / "notes.md"
            notes.write_text(current["body"].rstrip()
                + f"\n\n[夸克网盘下载]({share_url}) · [软件主页]({HOMEPAGE})\n", encoding="utf-8")
            github(args.gh, ["edit", tag, "--draft=false", "--latest", "--notes-file", str(notes)])
            if release_snapshot(args.gh, tag, names)[0]["isDraft"]:
                raise RuntimeError("GitHub 发布页仍为草稿，请检查后重试。")
    print(f"软件主页已生成 {version} 的下载入口，请将该版本部署到 Sites。")
    print(f"https://github.com/{REPOSITORY}/releases/tag/{tag}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tag", help="正式版本，例如 v2.0.0")
    parser.add_argument("--quark-cli", required=True, help="已授权的夸克官方 quark-drive.cjs 路径")
    parser.add_argument("--packages", type=Path, help="可复用本机已校验的软件包目录")
    parser.add_argument("--site", type=Path, required=True, help="已打开的墨墨搜词 Sites 源码目录")
    parser.add_argument("--gh", default="gh", help="GitHub CLI 路径")
    parser.add_argument("--node", default="node", help="Node.js 路径")
    try:
        publish(parser.parse_args())
    except (RuntimeError, OSError, KeyError, ValueError, subprocess.TimeoutExpired) as error:
        parser.exit(1, f"{error}\n")


if __name__ == "__main__":
    main()
