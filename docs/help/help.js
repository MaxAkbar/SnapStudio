"use strict";

(() => {
  const search = document.getElementById("search");
  const clear = document.getElementById("clear-search");
  const status = document.getElementById("search-status");
  const empty = document.getElementById("no-results");
  const topics = Array.from(document.querySelectorAll("main section"));
  const links = Array.from(document.querySelectorAll("nav a"));
  const searchable = new Map(topics.map(topic => [topic, topic.textContent.toLocaleLowerCase()]));

  function filterTopics() {
    const terms = search.value.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
    let count = 0;
    topics.forEach((topic, index) => {
      const matches = terms.every(term => searchable.get(topic).includes(term));
      topic.hidden = !matches;
      links[index].hidden = !matches;
      if (matches) count++;
    });
    empty.hidden = count !== 0;
    clear.hidden = terms.length === 0;
    status.textContent = terms.length ? `${count} of ${topics.length} topics found` : "";
  }

  function clearSearch() {
    search.value = "";
    filterTopics();
  }

  function selectTopic() {
    // A history navigation must also reveal a topic hidden by a later search.
    const active = topics.find(topic => `#${topic.id}` === location.hash);
    if (active?.hidden) clearSearch();
    links.forEach(link => {
      if (link.hash === location.hash) link.setAttribute("aria-current", "location");
      else link.removeAttribute("aria-current");
    });
    active?.scrollIntoView();
  }

  search.addEventListener("input", filterTopics);
  search.addEventListener("keydown", event => {
    if (event.key === "Escape") {
      clearSearch();
      event.preventDefault();
    }
  });
  clear.addEventListener("click", () => {
    clearSearch();
    search.focus();
  });
  document.querySelector(".brand").addEventListener("click", clearSearch);
  window.addEventListener("hashchange", selectTopic);
  selectTopic();
})();
