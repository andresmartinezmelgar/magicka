"""Stable-release identity shared by the legacy and generation-based updaters."""
import re

TAG = re.compile(r"v(\d+)\.(\d+)\.(\d+)")


def version(value):
    match = TAG.fullmatch("v" + str(value).removeprefix("v"))
    if not match:
        raise RuntimeError("Versión no reconocida: " + str(value))
    return tuple(map(int, match.groups()))


def stable_tag(release):
    tag = release.get("tag_name", "")
    if release.get("draft") is not False or release.get("prerelease") is not False or not TAG.fullmatch(tag):
        raise RuntimeError("GitHub no ha devuelto una Release estable válida.")
    return tag
