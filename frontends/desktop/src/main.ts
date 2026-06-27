import { createApp } from "vue";
import { addCollection } from "@iconify/vue";
import fluentIcons from "@iconify-json/fluent/icons.json";
import solarIcons from "@iconify-json/solar/icons.json";
import App from "./App.vue";
import "./styles.css";

addCollection(solarIcons);
addCollection(fluentIcons);

createApp(App).mount("#app");
