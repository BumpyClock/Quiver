/// <reference path="../node_modules/@types/chrome/index.d.ts"/>

chrome.runtime.onInstalled.addListener(function () {
  chrome.contextMenus.create({
    title: "Quiver the Page",
    contexts: ["page"],
    id: "quiver_page",
  });

  chrome.contextMenus.create({
    title: "Quiver the Link",
    contexts: ["link"],
    id: "quiver_link",
  });
});

chrome.contextMenus.onClicked.addListener(async (info, _) => {
  chrome.runtime.sendNativeMessage("com.bumpyclock.quiver", {
    url: info.linkUrl || info.pageUrl,
  });
});
