(() => {
  async function refresh() {
    const empty = document.getElementById("empty");
    try {
      const response = await fetch("/demo-api/inbox", { cache: "no-store" });
      if (!response.ok) throw new Error("Inbox unavailable");
      const entries = await response.json();
      const body = document.getElementById("entries");
      body.replaceChildren();
      empty.hidden = entries.length !== 0;
      empty.textContent = "No codes sent yet.";
      for (const entry of entries) {
        const row = document.createElement("tr");
        const address = document.createElement("td");
        address.textContent = entry.address;
        const codeCell = document.createElement("td");
        const code = document.createElement("code");
        code.textContent = entry.code;
        codeCell.append(code);
        const sent = document.createElement("td");
        sent.textContent = new Date(entry.sentAt).toLocaleTimeString();
        row.append(address, codeCell, sent);
        body.append(row);
      }
    } catch {
      empty.hidden = false;
      empty.textContent = "Inbox unavailable. Retrying shortly.";
    } finally {
      window.setTimeout(refresh, 2000);
    }
  }
  refresh();
})();