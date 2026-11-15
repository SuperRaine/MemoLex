"""Files shared by the portable and installer payloads."""
from pathlib import PurePath

def include_file(relative, include_browser_runtime=False):
    path=PurePath(relative)
    if not include_browser_runtime and path.parts and path.parts[0].casefold()=='webview2':return False
    if path.suffix.casefold()=='.pdb' or path.name.casefold()=='smoke-test.json':return False
    return True
