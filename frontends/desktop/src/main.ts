import { createApp } from "vue";
import { addCollection } from "@iconify/vue";
import { iconCollections } from "./generatedIconCollections";
import App from "./App.vue";
import "./styles.css";

iconCollections.forEach((collection) => addCollection(collection));

createApp(App).mount("#app");
