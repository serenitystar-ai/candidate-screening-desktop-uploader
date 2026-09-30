interface WebViewHost {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
}

interface Window {
  chrome?: { webview?: WebViewHost };
}
