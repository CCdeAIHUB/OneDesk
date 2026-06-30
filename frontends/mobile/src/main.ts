import { createApp } from "vue";
import { addCollection } from "@iconify/vue";
import solarIcons from "@iconify-json/solar/icons.json";
import App from "./App.vue";
import "./styles.css";

addCollection(solarIcons);

createApp(App).mount("#app");
